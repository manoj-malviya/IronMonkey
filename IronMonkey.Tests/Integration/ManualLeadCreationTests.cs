using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class ManualLeadCreationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public ManualLeadCreationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WhenNoDuplicates_ReturnsCreatedLead() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WhenDuplicateExists_ReturnsDuplicateWarning() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WithForceCreate_CreatesLeadDespiteDuplicate() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WithCustomFields_StoresCustomValues() { await Task.CompletedTask; }
}
