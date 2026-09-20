using IronMonkey.Data.Presentation;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Terminology resolution. The failure this guards against is a blank label: every term has a
/// built-in default, and every path that could return nothing must fall back to it instead.
/// </summary>
public class TenantTerminologyTests
{
    [Fact]
    public void NoOverrides_UsesBuiltInDefaults()
    {
        var resolved = ResolvedTerminology.From(null);

        Assert.Equal("Lead", resolved.Singular(TerminologyTerm.Lead));
        Assert.Equal("Leads", resolved.Plural(TerminologyTerm.Lead));
        Assert.Equal("Opportunity", resolved.Singular(TerminologyTerm.Opportunity));
    }

    [Fact]
    public void EveryTerm_HasANonEmptyDefault()
    {
        // A term added to the enum without a default would surface as an empty label in the
        // UI rather than as a compile error, so assert the whole set rather than samples.
        var resolved = ResolvedTerminology.Default;

        foreach (var term in TerminologyDefaults.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(resolved.Singular(term)), $"{term} singular");
            Assert.False(string.IsNullOrWhiteSpace(resolved.Plural(term)), $"{term} plural");
        }
    }

    [Fact]
    public void Override_ReplacesBothForms()
    {
        var resolved = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
        });

        Assert.Equal("Enquiry", resolved.Singular(TerminologyTerm.Lead));
        Assert.Equal("Enquiries", resolved.Plural(TerminologyTerm.Lead));
    }

    [Fact]
    public void PluralIsNeverDerivedFromSingular()
    {
        // The whole reason plurals are stored separately: appending "s" gives "Enquirys".
        var resolved = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "Enquiry" }
        });

        Assert.Equal("Enquiry", resolved.Singular(TerminologyTerm.Lead));
        // Only the singular was overridden, so the plural keeps the built-in default rather
        // than becoming "Enquirys".
        Assert.Equal("Leads", resolved.Plural(TerminologyTerm.Lead));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankOverride_FallsBackRatherThanRenderingEmpty(string? blank)
    {
        var resolved = ResolvedTerminology.From(new TenantTerminology
        {
            Contact = new TermOverride { Singular = blank, Plural = blank }
        });

        Assert.Equal("Contact", resolved.Singular(TerminologyTerm.Contact));
        Assert.Equal("Contacts", resolved.Plural(TerminologyTerm.Contact));
    }

    [Fact]
    public void Override_IsTrimmed()
    {
        var resolved = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "  Applicant  " }
        });

        Assert.Equal("Applicant", resolved.Singular(TerminologyTerm.Lead));
    }

    [Theory]
    [InlineData(0, "Enquiries")]
    [InlineData(1, "Enquiry")]
    [InlineData(2, "Enquiries")]
    public void ForCount_PicksTheRightForm(int count, string expected)
    {
        var resolved = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
        });

        Assert.Equal(expected, resolved.ForCount(TerminologyTerm.Lead, count));
    }

    [Fact]
    public void OneTenantsTermsDoNotLeakIntoAnothers()
    {
        // Resolution is pure and per-call, so two tenants resolved in the same process must
        // not share state — this is the cheap guard against a static cache being added later.
        var dealership = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
        });

        var university = ResolvedTerminology.From(new TenantTerminology
        {
            Lead = new TermOverride { Singular = "Applicant", Plural = "Applicants" }
        });

        Assert.Equal("Enquiries", dealership.Plural(TerminologyTerm.Lead));
        Assert.Equal("Applicants", university.Plural(TerminologyTerm.Lead));
        Assert.Equal("Leads", ResolvedTerminology.Default.Plural(TerminologyTerm.Lead));
    }
}
