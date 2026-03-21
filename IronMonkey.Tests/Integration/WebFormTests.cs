using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class WebFormTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public WebFormTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task CreateWebForm_WhenAuthenticated_ReturnsFormUrlWithToken() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task SubmitForm_WithValidToken_CreatesLeadWithWebFormSource() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task SubmitForm_WithHoneypotFilled_ReturnsBadRequest() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task SubmitForm_WithInvalidToken_ReturnsBadRequest() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task SubmitForm_WhenDuplicateLeadFound_CreatesLeadAndFlagsAsPotentialDuplicate() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task GetFormPage_WithValidToken_ReturnsHtmlWithHoneypot() { await Task.CompletedTask; }
}
