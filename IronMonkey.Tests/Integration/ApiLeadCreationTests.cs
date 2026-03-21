using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class ApiLeadCreationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public ApiLeadCreationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WithValidApiKey_Returns201WithDuplicatesArray() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WithMissingApiKey_Returns401() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WithInvalidApiKey_Returns401() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateLead_WhenDuplicateFound_IncludesDuplicatesInResponse() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task GenerateApiKey_WhenAuthenticated_ReturnsKey() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task DeleteApiKey_WhenAuthenticated_KeyNoLongerAcceptsRequests() { await Task.CompletedTask; }
}
