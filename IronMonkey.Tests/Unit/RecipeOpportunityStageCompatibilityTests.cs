using System.Text.Json;
using IronMonkey.Common;
using IronMonkey.Data.RecipeContent;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Recipes are stored as a JSONB snapshot in the central catalog, so adding a section to the
/// content model is a compatibility question: documents written before it existed must keep
/// deserializing and provisioning. A regression here breaks signup for every vertical.
///
/// Follows the precedent set by the nullable <c>Presentation</c> property and the tests
/// alongside it in <see cref="RecipePresentationCompatibilityTests"/>.
/// </summary>
public class RecipeOpportunityStageCompatibilityTests
{
    /// <summary>The exact shape the catalog held before opportunity stages existed.</summary>
    private const string LegacyJson = """
    {
      "PipelineStages": [
        { "Name": "New", "Order": 1, "StageType": "Entry" },
        { "Name": "Qualified", "Order": 2, "StageType": "Active" }
      ],
      "CustomFields": [ { "FieldName": "Budget", "FieldType": "Currency", "IsRequired": false, "Options": [] } ],
      "WorkflowRules": [],
      "Roles": [],
      "SampleLeads": []
    }
    """;

    [Fact]
    public void A_recipe_stored_before_opportunity_stages_existed_still_deserializes()
    {
        var content = JsonSerializer.Deserialize<RecipeContentModel>(LegacyJson);

        Assert.NotNull(content);
        Assert.Equal(2, content!.PipelineStages.Count);
        Assert.Single(content.CustomFields);

        // Absent, not empty — the distinction provisioning depends on.
        Assert.Null(content.OpportunityStages);
    }

    /// <summary>
    /// The distinction that matters at the provisioning site. Null means "this recipe has no
    /// opinion", and the tenant gets the product defaults so it can create a deal on day one.
    /// An explicitly empty list means "seed none", which is a deliberate authoring choice.
    /// Collapsing the two would either strand legacy tenants with no opportunity stages or
    /// override an author who meant to have none.
    /// </summary>
    [Fact]
    public void Null_means_use_the_defaults_and_empty_means_seed_none()
    {
        var legacy = JsonSerializer.Deserialize<RecipeContentModel>(LegacyJson)!;
        var explicitlyEmpty = JsonSerializer.Deserialize<RecipeContentModel>("""
            {"PipelineStages":[],"CustomFields":[],"WorkflowRules":[],"Roles":[],"SampleLeads":[],
             "OpportunityStages":[]}
            """)!;

        Assert.Null(legacy.OpportunityStages);
        Assert.NotNull(explicitlyEmpty.OpportunityStages);
        Assert.Empty(explicitlyEmpty.OpportunityStages!);

        // What provisioning resolves each to.
        Assert.Equal(OpportunityStages.Defaults.Count, Resolve(legacy).Count);
        Assert.Empty(Resolve(explicitlyEmpty));
    }

    /// <summary>Mirrors the fallback in TenantProvisioningService.</summary>
    private static List<PipelineStageDefinition> Resolve(RecipeContentModel content) =>
        content.OpportunityStages
        ?? [.. OpportunityStages.Defaults.Select(d =>
            new PipelineStageDefinition { Name = d.Name, Order = d.Order, StageType = d.StageType })];

    [Fact]
    public void The_default_fallback_carries_real_terminal_stage_types()
    {
        var resolved = Resolve(JsonSerializer.Deserialize<RecipeContentModel>(LegacyJson)!);

        // A default set with no terminal stage would leave a legacy tenant unable to close a
        // deal at all, since closing resolves by type.
        Assert.Contains(resolved, s => s.StageType == "ClosedWon");
        Assert.Contains(resolved, s => s.StageType == "ClosedLost");
        Assert.Contains(resolved, s => s.StageType == "Entry");

        // Orders are a dense 1..n sequence, as the reorder endpoint expects.
        Assert.Equal(
            Enumerable.Range(1, resolved.Count),
            resolved.OrderBy(s => s.Order).Select(s => s.Order));
    }

    [Fact]
    public void A_recipe_with_opportunity_stages_round_trips_through_json()
    {
        var original = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "Enquiry", Order = 1, StageType = "Entry" }],
            OpportunityStages =
            [
                new PipelineStageDefinition { Name = "Test Drive", Order = 1, StageType = "Entry" },
                new PipelineStageDefinition { Name = "Finance Approved", Order = 2, StageType = "Active" },
                new PipelineStageDefinition { Name = "Delivered", Order = 3, StageType = "ClosedWon" },
                new PipelineStageDefinition { Name = "Walked Away", Order = 4, StageType = "ClosedLost" }
            ]
        };

        var round = JsonSerializer.Deserialize<RecipeContentModel>(JsonSerializer.Serialize(original))!;

        Assert.Equal(4, round.OpportunityStages!.Count);
        Assert.Equal("Test Drive", round.OpportunityStages[0].Name);
        Assert.Equal("ClosedWon", round.OpportunityStages[2].StageType);

        // The vertical's own deal stages, sharing no names with the product defaults — which
        // is the entire point of making them configurable.
        Assert.Empty(round.OpportunityStages.Select(s => s.Name).Intersect(OpportunityStages.All));

        // The lead stages are unaffected by the new section.
        Assert.Single(round.PipelineStages);
        Assert.Equal("Enquiry", round.PipelineStages[0].Name);
    }

    /// <summary>
    /// The two stage kinds reuse one definition type rather than declaring a parallel shape,
    /// so a recipe's lead and opportunity sections deserialize identically.
    /// </summary>
    [Fact]
    public void Both_stage_sections_use_the_same_definition_shape()
    {
        const string json = """
        {
          "PipelineStages":     [ { "Name": "New",   "Order": 1, "StageType": "Entry" } ],
          "OpportunityStages":  [ { "Name": "Intro", "Order": 1, "StageType": "Entry" } ],
          "CustomFields": [], "WorkflowRules": [], "Roles": [], "SampleLeads": []
        }
        """;

        var content = JsonSerializer.Deserialize<RecipeContentModel>(json)!;

        Assert.IsType<PipelineStageDefinition>(content.PipelineStages[0]);
        Assert.IsType<PipelineStageDefinition>(content.OpportunityStages![0]);
        Assert.Equal(content.PipelineStages[0].StageType, content.OpportunityStages[0].StageType);
    }
}
