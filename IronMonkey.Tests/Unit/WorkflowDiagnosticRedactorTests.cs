using IronMonkey.Data.Workflow;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// The redactor is the only thing standing between an exception message and a table a tenant
/// Admin can read and export, so each class of secret it is supposed to strip gets a test.
/// </summary>
public class WorkflowDiagnosticRedactorTests
{
    [Fact]
    public void Redact_StripsQueryStringFromUrl()
    {
        // The most common leak: a webhook authenticated by a token in the query string, quoted
        // back by the HTTP client's own exception message.
        var result = WorkflowDiagnosticRedactor.Redact(
            "Connection refused for https://hooks.example.com/inbound?token=s3cr3t-value&id=42");

        Assert.NotNull(result);
        Assert.DoesNotContain("s3cr3t-value", result);
        Assert.DoesNotContain("token=", result);
        // The host and path survive — they are what makes the failure diagnosable.
        Assert.Contains("https://hooks.example.com/inbound", result);
    }

    [Fact]
    public void Redact_StripsUserInfoFromUrl()
    {
        var result = WorkflowDiagnosticRedactor.Redact("POST to https://user:hunter2@api.example.com/hook failed");

        Assert.NotNull(result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("user:", result);
        Assert.Contains("api.example.com", result);
    }

    [Theory]
    [InlineData("X-Api-Key: abc123def456", "abc123def456")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("{\"password\":\"correct-horse\"}", "correct-horse")]
    [InlineData("client_secret=shhh-dont-tell", "shhh-dont-tell")]
    [InlineData("{\"X-Signature\":\"deadbeefcafe\"}", "deadbeefcafe")]
    public void Redact_RemovesSensitiveValues(string input, string secret)
    {
        var result = WorkflowDiagnosticRedactor.Redact(input);

        Assert.NotNull(result);
        Assert.DoesNotContain(secret, result);
    }

    [Fact]
    public void Redact_RemovesBareBearerToken()
    {
        // A credential with no property name in front of it — how ASP.NET and Npgsql
        // exceptions frequently render an auth header.
        var result = WorkflowDiagnosticRedactor.Redact("Rejected credential Bearer abcdef1234567890xyz");

        Assert.NotNull(result);
        Assert.DoesNotContain("abcdef1234567890xyz", result);
    }

    [Fact]
    public void Redact_CollapsesNewlinesAndTruncates()
    {
        var long_ = "Something failed: " + new string('x', 900);
        var result = WorkflowDiagnosticRedactor.Redact("line one\r\nline two\n" + long_);

        Assert.NotNull(result);
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("\r", result);
        // Bounded so a stack trace cannot become tenant history.
        Assert.True(result.Length <= WorkflowDiagnosticRedactor.MaxMessageLength + 1,
            $"expected <= {WorkflowDiagnosticRedactor.MaxMessageLength + 1}, got {result.Length}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_ReturnsNullForBlank(string? input)
    {
        // Null rather than "" so an absent message stays absent instead of rendering as a
        // blank row in the timeline.
        Assert.Null(WorkflowDiagnosticRedactor.Redact(input));
    }

    [Fact]
    public void Redact_LeavesOrdinaryMessageIntact()
    {
        const string message = "Webhook returned HTTP 503 Service Unavailable.";
        Assert.Equal(message, WorkflowDiagnosticRedactor.Redact(message));
    }

    [Fact]
    public void SafeUrl_KeepsSchemeHostPathAndDropsQuery()
    {
        var result = WorkflowDiagnosticRedactor.SafeUrl("https://example.com:8443/a/b?secret=1");

        Assert.StartsWith("https://example.com:8443/a/b", result);
        Assert.DoesNotContain("secret=1", result);
    }

    [Fact]
    public void SafeUrl_ReturnsPlaceholderForUnparseableValue()
    {
        Assert.Equal("[redacted]", WorkflowDiagnosticRedactor.SafeUrl("not a url at all"));
    }

    [Fact]
    public void SafeHost_OmitsPathAndQuery()
    {
        var result = WorkflowDiagnosticRedactor.SafeHost(new Uri("https://hooks.example.com/path?x=1"));

        Assert.Equal("https://hooks.example.com", result);
    }

    [Theory]
    [InlineData("jane.doe@example.com", "j***@example.com")]
    [InlineData("a@example.com", "a***@example.com")]
    public void MaskEmail_KeepsDomainAndFirstCharacter(string input, string expected)
    {
        // The domain is kept because a rule mailing the wrong domain is the failure an Admin
        // needs to see; the local part is the personal data.
        Assert.Equal(expected, WorkflowDiagnosticRedactor.MaskEmail(input));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("trailing@")]
    public void MaskEmail_MasksNonEmailWholesale(string input)
    {
        // A misconfigured "to" can hold anything, including a token, so a value that is not an
        // address is never echoed back.
        Assert.Equal("***", WorkflowDiagnosticRedactor.MaskEmail(input));
    }

    [Fact]
    public void MaskEmail_ReturnsNullForBlank()
    {
        Assert.Null(WorkflowDiagnosticRedactor.MaskEmail(null));
        Assert.Null(WorkflowDiagnosticRedactor.MaskEmail("  "));
    }
}
