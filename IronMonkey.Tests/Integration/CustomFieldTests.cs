using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-01: Custom field definitions are tenant-scoped and type-enforced
[Collection("Integration")]
public class CustomFieldTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task CanCreateCustomTextField_ForTenant()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task CanCreateCustomDropdownField_WithOptions()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task ListCustomFields_ReturnsOnlyTenantFields()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task CustomFieldValue_IsValidatedAgainstType()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }
}
