using System.Text.Json;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.Data.Entities;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Condition matching and placeholder interpolation. The engine swallows rule failures by
/// design, so a wrong condition shows up as a rule that silently never fires — these pin the
/// behaviour that the help documentation promises tenants.
/// </summary>
public class WorkflowConditionTests
{
    private static Lead MakeLead(
        string source = "Manual", string email = "sam@acme.com", int ageDays = 0)
    {
        var lead = Lead.Create(
            Guid.NewGuid(), "Sam", "Rivera", "555-0100", email,
            Enum.Parse<LeadSource>(source), Guid.NewGuid());
        lead.CreatedAt = DateTime.UtcNow.AddDays(-ageDays);
        return lead;
    }

    [Theory]
    [InlineData("""{"always":true}""", true)]
    [InlineData("""{"field":"Source","equals":"Manual"}""", true)]
    [InlineData("""{"field":"Source","equals":"Api"}""", false)]
    [InlineData("""{"field":"Source","notEquals":"Api"}""", true)]
    [InlineData("""{"field":"Email","contains":"@acme.com"}""", true)]
    [InlineData("""{"field":"Email","contains":"@other.com"}""", false)]
    public void Field_conditions_match_expected(string json, bool expected)
        => Assert.Equal(expected, WorkflowRuleEngine.EvaluateCondition(json, MakeLead()));

    [Fact]
    public void Malformed_json_never_fires_the_action()
        => Assert.False(WorkflowRuleEngine.EvaluateCondition("{not json", MakeLead()));

    [Theory]
    [InlineData(0, false)]  // created just now — not yet old enough
    [InlineData(5, true)]   // five days old — matches olderThanDays: 3
    public void OlderThanDays_gates_on_lead_age(int ageDays, bool expected)
    {
        var lead = MakeLead(ageDays: ageDays);
        Assert.Equal(expected, WorkflowRuleEngine.EvaluateCondition("""{"olderThanDays":3}""", lead));
    }

    [Fact]
    public void All_requires_every_nested_condition()
    {
        var lead = MakeLead(source: "Api", ageDays: 10);

        Assert.True(WorkflowRuleEngine.EvaluateCondition(
            """{"all":[{"field":"Source","equals":"Api"},{"olderThanDays":3}]}""", lead));

        Assert.False(WorkflowRuleEngine.EvaluateCondition(
            """{"all":[{"field":"Source","equals":"WebForm"},{"olderThanDays":3}]}""", lead));
    }

    [Fact]
    public void Any_requires_only_one_nested_condition()
    {
        var lead = MakeLead(source: "Api");

        Assert.True(WorkflowRuleEngine.EvaluateCondition(
            """{"any":[{"field":"Source","equals":"WebForm"},{"field":"Source","equals":"Api"}]}""", lead));
    }

    [Fact]
    public void Custom_field_conditions_read_the_jsonb_bag()
    {
        var lead = MakeLead();
        var definitionId = Guid.NewGuid().ToString();
        lead.CustomFields.Set(definitionId, JsonSerializer.Deserialize<JsonElement>("\"Swift\""));

        Assert.True(WorkflowRuleEngine.EvaluateCondition(
            $$"""{"customField":"{{definitionId}}","equals":"Swift"}""", lead));

        Assert.False(WorkflowRuleEngine.EvaluateCondition(
            $$"""{"customField":"{{definitionId}}","equals":"Baleno"}""", lead));
    }

    [Fact]
    public void Placeholders_are_replaced_with_lead_values()
    {
        var lead = MakeLead(email: "sam@acme.com");

        Assert.Equal(
            "Hi Sam Rivera (sam@acme.com) from Manual",
            WorkflowRuleEngine.Interpolate("Hi {{FullName}} ({{Email}}) from {{Source}}", lead));
    }
}
