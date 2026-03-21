using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-05: Lead merge preserves surviving lead fields and creates audit record
[Collection("Integration")]
public class LeadMergeTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task MergeLeads_SurvivingLeadRetainsSourceFields()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task MergeLeads_TargetLeadIsSoftDeleted()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task MergeLeads_AuditRecordCreated_WithBothLeadIds()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task MergeLeads_SurvivingLeadCustomFieldsIncludeTargetValues()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }
}
