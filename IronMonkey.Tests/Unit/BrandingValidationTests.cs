using IronMonkey.Data.Presentation;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Branding is tenant-supplied text that is rendered into a page every user in the tenant
/// loads, so it is validated on write rather than sanitized on read. These pin the rejections
/// — a regression here is stored XSS, not a cosmetic bug.
/// </summary>
public class BrandingValidationTests
{
    [Theory]
    [InlineData("#fff")]
    [InlineData("#1d4ed8")]
    [InlineData("#1D4ED8")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidColors_AreAccepted(string? color)
    {
        Assert.True(BrandingValidation.IsValidColor(color));
    }

    [Theory]
    // A CSS payload smuggled through a colour field is the classic way to break out of a
    // style attribute into behaviour.
    [InlineData("red; background: url(javascript:alert(1))")]
    [InlineData("url(https://evil.example/x)")]
    [InlineData("expression(alert(1))")]
    [InlineData("#12345")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("red")]
    public void InvalidColors_AreRejected(string color)
    {
        Assert.False(BrandingValidation.IsValidColor(color));
    }

    [Theory]
    [InlineData("/img/logo.png")]
    [InlineData("https://cdn.example.com/logo.svg")]
    [InlineData(null)]
    public void ValidLogoUrls_AreAccepted(string? url)
    {
        Assert.True(BrandingValidation.IsValidLogoUrl(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48c2NyaXB0PmFsZXJ0KDEpPC9zY3JpcHQ+PC9zdmc+")]
    // Protocol-relative: inherits the page scheme and loads a third-party asset.
    [InlineData("//evil.example/logo.png")]
    // Traversal out of the site root.
    [InlineData("/../../etc/passwd")]
    // Plain http to a remote host would be mixed content in production.
    [InlineData("http://cdn.example.com/logo.png")]
    public void DangerousLogoUrls_AreRejected(string url)
    {
        Assert.False(BrandingValidation.IsValidLogoUrl(url));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Acme \" onload=\"alert(1)")]
    [InlineData("Acme & Sons")]
    public void DisplayNamesWithMarkupCharacters_AreRejected(string name)
    {
        Assert.False(BrandingValidation.IsValidDisplayName(name));
    }

    [Fact]
    public void OrdinaryDisplayName_IsAccepted()
    {
        Assert.True(BrandingValidation.IsValidDisplayName("Northgate Motors"));
    }

    [Fact]
    public void OverlongDisplayName_IsRejected()
    {
        var tooLong = new string('a', BrandingValidation.MaxDisplayNameLength + 1);
        Assert.False(BrandingValidation.IsValidDisplayName(tooLong));
    }

    [Fact]
    public void Validate_ReportsTheFirstProblem()
    {
        var problem = BrandingValidation.Validate(new TenantBranding
        {
            DisplayName = "Fine",
            PrimaryColor = "red; background: url(x)"
        });

        Assert.NotNull(problem);
        Assert.Contains("hex", problem);
    }

    [Fact]
    public void Validate_AcceptsFullyValidBranding()
    {
        var problem = BrandingValidation.Validate(new TenantBranding
        {
            DisplayName = "Northgate Motors",
            LogoUrl = "/img/northgate.png",
            PrimaryColor = "#1d4ed8",
            AccentColor = "#4f46e5"
        });

        Assert.Null(problem);
    }

    [Fact]
    public void Validate_AcceptsNullBranding()
    {
        Assert.Null(BrandingValidation.Validate(null));
    }
}
