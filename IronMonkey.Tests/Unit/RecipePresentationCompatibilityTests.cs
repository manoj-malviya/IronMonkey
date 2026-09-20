using System.Text.Json;
using IronMonkey.Data.Presentation;
using IronMonkey.Data.RecipeContent;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Recipes are stored as a JSONB snapshot in the central catalog, so adding a section to the
/// content model is a compatibility question: documents written before it existed must keep
/// deserializing and provisioning. A regression here breaks signup for every vertical.
/// </summary>
public class RecipePresentationCompatibilityTests
{
    [Fact]
    public void RecipeStoredBeforePresentationExisted_StillDeserializes()
    {
        // Exactly the shape the catalog held before this change: no presentation key at all.
        const string legacyJson = """
        {
          "PipelineStages": [ { "Name": "New", "Order": 1, "StageType": "Entry" } ],
          "CustomFields": [ { "FieldName": "Budget", "FieldType": "Currency", "IsRequired": false, "Options": [] } ],
          "WorkflowRules": [],
          "Roles": [],
          "SampleLeads": []
        }
        """;

        var content = JsonSerializer.Deserialize<RecipeContentModel>(legacyJson);

        Assert.NotNull(content);
        Assert.Single(content!.PipelineStages);
        Assert.Single(content.CustomFields);
        // Absent rather than empty: provisioning skips the copy and the tenant keeps defaults.
        Assert.Null(content.Presentation);
    }

    [Fact]
    public void LegacyRecipe_LeavesTenantOnBuiltInDefaults()
    {
        var content = JsonSerializer.Deserialize<RecipeContentModel>(
            """{"PipelineStages":[],"CustomFields":[],"WorkflowRules":[],"Roles":[],"SampleLeads":[]}""");

        var presentation = TenantPresentation.From(content!.Presentation is null
            ? null
            : new TenantPresentationSettings { Terminology = content.Presentation.Terminology });

        Assert.Equal("Lead", presentation.Terminology.Singular(TerminologyTerm.Lead));
        Assert.Equal("Leads", presentation.Terminology.Plural(TerminologyTerm.Lead));
    }

    [Fact]
    public void RecipeWithPresentation_RoundTripsThroughJson()
    {
        var original = new RecipeContentModel
        {
            Presentation = new RecipePresentationDefinition
            {
                Terminology = new TenantTerminology
                {
                    Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
                },
                Locale = new TenantLocale { CurrencyCode = "GBP", CurrencySymbol = "£" }
            }
        };

        var round = JsonSerializer.Deserialize<RecipeContentModel>(JsonSerializer.Serialize(original));

        Assert.Equal("Enquiry", round!.Presentation!.Terminology!.Lead!.Singular);
        Assert.Equal("Enquiries", round.Presentation.Terminology.Lead.Plural);
        Assert.Equal("£", round.Presentation.Locale!.CurrencySymbol);
    }

    [Fact]
    public void RecipeTerminology_ResolvesToTheVerticalsWords()
    {
        // What an Automobile tenant should see immediately after provisioning, with no setup.
        var recipe = new RecipePresentationDefinition
        {
            Terminology = new TenantTerminology
            {
                Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
            }
        };

        var presentation = TenantPresentation.From(new TenantPresentationSettings
        {
            Terminology = recipe.Terminology,
            Locale = recipe.Locale
        });

        Assert.Equal("Enquiries", presentation.Terminology.Plural(TerminologyTerm.Lead));
        // Untouched terms still resolve, rather than coming back blank.
        Assert.Equal("Contacts", presentation.Terminology.Plural(TerminologyTerm.Contact));
    }
}
