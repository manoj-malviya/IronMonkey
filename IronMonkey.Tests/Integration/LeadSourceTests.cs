using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-03: Lead source is stored and surfaced on lead detail
[Collection("Integration")]
public class LeadSourceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task CreateLead_WithManualSource_SourceStoredCorrectly()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task LeadDetail_IncludesSourceField()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }
}
