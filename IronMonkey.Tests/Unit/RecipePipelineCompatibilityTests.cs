using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data.Entities;
using IronMonkey.Data.RecipeContent;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Recipe backward compatibility for multiple pipelines.
///
/// Every recipe already stored in the platform catalog predates the <c>Pipelines</c> section
/// and carries null there. Those documents must keep provisioning, and must keep provisioning
/// to exactly the single-pipeline tenant they always produced — a recipe that suddenly seeded
/// two funnels, or none, would change what every existing vertical means.
///
/// These run against <see cref="TenantProvisioningService.BuildPipelineDefinitions"/> directly:
/// it is the one place the compatibility rules live, so it is the thing worth pinning, and it
/// needs no database.
/// </summary>
public class RecipePipelineCompatibilityTests
{
    [Fact]
    public void A_recipe_with_no_pipelines_section_seeds_one_default_per_record_type()
    {
        // The shape of every recipe stored before this existed.
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new() { Name = "Enquiry", Order = 1, StageType = "Entry" },
                new() { Name = "Qualified", Order = 2, StageType = "Active" }
            ]
        };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);

        Assert.Equal(2, definitions.Count);

        var lead = definitions.Single(d => d.RecordType == nameof(PipelineRecordType.Lead));
        var opportunity = definitions.Single(d => d.RecordType == nameof(PipelineRecordType.Opportunity));

        // The flat stage list became the default lead pipeline, unchanged and in order —
        // which is precisely what that list meant before pipelines existed.
        Assert.True(lead.IsDefault);
        Assert.Equal(["Enquiry", "Qualified"], lead.Stages.Select(s => s.Name));

        // And the opportunity side keeps Part A's null-means-defaults rule.
        Assert.True(opportunity.IsDefault);
        Assert.NotEmpty(opportunity.Stages);
    }

    [Fact]
    public void A_null_opportunity_stage_list_still_means_product_defaults()
    {
        // Part A's load-bearing distinction, preserved: null is "no opinion", and provisioning
        // applies the built-in set rather than leaving the tenant unable to create a deal.
        var content = new RecipeContentModel { OpportunityStages = null };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);
        var opportunity = definitions.Single(d => d.RecordType == nameof(PipelineRecordType.Opportunity));

        Assert.NotEmpty(opportunity.Stages);
        Assert.Contains(opportunity.Stages, s => s.StageType == nameof(StageType.ClosedWon));
        Assert.Contains(opportunity.Stages, s => s.StageType == nameof(StageType.ClosedLost));
    }

    [Fact]
    public void An_explicitly_empty_opportunity_stage_list_is_honoured_as_none()
    {
        // The other half of that distinction. An empty list is a deliberate authoring choice,
        // unlike absence, so it is respected — the pipeline is still created (the tenant can
        // add stages later) but arrives with none, as authored.
        var content = new RecipeContentModel { OpportunityStages = [] };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);
        var opportunity = definitions.Single(d => d.RecordType == nameof(PipelineRecordType.Opportunity));

        Assert.Empty(opportunity.Stages);
    }

    [Fact]
    public void A_recipe_that_names_pipelines_seeds_them_and_ignores_the_flat_lists()
    {
        // A half-specified recipe must not produce a surprising merge of the two shapes.
        var content = new RecipeContentModel
        {
            PipelineStages = [new() { Name = "IGNORED", Order = 1, StageType = "Entry" }],
            Pipelines =
            [
                new()
                {
                    Name = "New car sales",
                    RecordType = "Lead",
                    Order = 1,
                    IsDefault = true,
                    Stages =
                    [
                        new() { Name = "Enquiry", Order = 1, StageType = "Entry" },
                        new() { Name = "Test drive", Order = 2, StageType = "Active" }
                    ]
                },
                new()
                {
                    Name = "Service bookings",
                    RecordType = "Lead",
                    Order = 2,
                    Stages = [new() { Name = "Booked", Order = 1, StageType = "Entry" }]
                }
            ]
        };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);

        Assert.Equal(2, definitions.Count);
        Assert.DoesNotContain(definitions.SelectMany(d => d.Stages), s => s.Name == "IGNORED");

        var sales = definitions.Single(d => d.Name == "New car sales");
        Assert.True(sales.IsDefault);
        Assert.Equal(["Enquiry", "Test drive"], sales.Stages.Select(s => s.Name));

        var service = definitions.Single(d => d.Name == "Service bookings");
        Assert.False(service.IsDefault);
    }

    [Fact]
    public void Exactly_one_pipeline_per_record_type_is_marked_default()
    {
        // A record type with no default has no answer for "no pipeline specified", which
        // every existing caller relies on; two defaults make the choice arbitrary, and the
        // filtered unique index rejects them outright.
        var content = new RecipeContentModel
        {
            Pipelines =
            [
                new() { Name = "A", RecordType = "Lead", Order = 1, IsDefault = true,
                        Stages = [new() { Name = "One", Order = 1, StageType = "Entry" }] },
                new() { Name = "B", RecordType = "Lead", Order = 2, IsDefault = true,
                        Stages = [new() { Name = "One", Order = 1, StageType = "Entry" }] },
                new() { Name = "C", RecordType = "Opportunity", Order = 1,
                        Stages = [new() { Name = "One", Order = 1, StageType = "Entry" }] }
            ]
        };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);

        foreach (var group in definitions.GroupBy(d => d.RecordType))
            Assert.Single(group.Where(d => d.IsDefault));

        // The first by order wins when a recipe wrongly marks several.
        Assert.True(definitions.Single(d => d.Name == "A").IsDefault);

        // And a record type that marked none still gets one.
        Assert.True(definitions.Single(d => d.Name == "C").IsDefault);
    }

    [Fact]
    public void A_pipeline_with_no_stages_falls_back_to_the_product_defaults()
    {
        // A pipeline with no stages accepts no records — it would appear in the picker and
        // then fail every create. Unlike the opportunity stage list, "a pipeline with nothing
        // in it" is not a coherent authoring intent.
        var content = new RecipeContentModel
        {
            Pipelines = [new() { Name = "Empty", RecordType = "Opportunity", Order = 1, Stages = [] }]
        };

        var definitions = TenantProvisioningService.BuildPipelineDefinitions(content);

        Assert.NotEmpty(definitions.Single().Stages);
    }

    [Fact]
    public void A_completely_absent_recipe_still_produces_both_default_pipelines()
    {
        // Provisioning with no recipe at all — the Blank path.
        var definitions = TenantProvisioningService.BuildPipelineDefinitions(null);

        Assert.Equal(2, definitions.Count);
        Assert.All(definitions, d => Assert.True(d.IsDefault));
        Assert.All(definitions, d => Assert.NotEmpty(d.Stages));
    }
}
