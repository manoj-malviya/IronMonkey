using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-04: Duplicate detection surfaces candidates by email, phone, and fuzzy name match
[Collection("Integration")]
public class DuplicateDetectionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task DuplicateCheck_OnMatchingEmail_ReturnsCandidates()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task DuplicateCheck_OnMatchingPhone_ReturnsCandidates()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task DuplicateCheck_OnSimilarName_ReturnsFuzzyMatch()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task DuplicateCheck_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }
}
