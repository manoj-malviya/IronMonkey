using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Presentation settings live in a jsonb column on the central tenant row, behind a value
/// converter. That combination has two failure modes worth pinning: a document that does not
/// survive the round trip, and an in-place edit that change tracking never notices because the
/// comparer compares by reference.
/// </summary>
[Collection("Integration")]
public class TenantPresentationPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantPresentationPersistenceTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private CentralDbContext CreateCentralDbContext()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    private static Tenant NewTenant(string name) =>
        Tenant.Create(name, $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}", "Standard", "Active");

    [Fact]
    public async Task Presentation_RoundTripsThroughJsonb()
    {
        await using var db = CreateCentralDbContext();
        await db.Database.MigrateAsync();

        var tenant = NewTenant("northgate");
        tenant.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = new TenantTerminology
            {
                Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" }
            },
            Locale = new TenantLocale { CurrencyCode = "GBP", CurrencySymbol = "£", TimeZoneId = "Europe/London" },
            Branding = new TenantBranding { DisplayName = "Northgate Motors", PrimaryColor = "#1d4ed8" }
        });

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await using var read = CreateCentralDbContext();
        var found = await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);

        Assert.Equal("Enquiry", found.Presentation!.Terminology!.Lead!.Singular);
        Assert.Equal("Enquiries", found.Presentation.Terminology.Lead.Plural);
        Assert.Equal("£", found.Presentation.Locale!.CurrencySymbol);
        Assert.Equal("Europe/London", found.Presentation.Locale.TimeZoneId);
        Assert.Equal("Northgate Motors", found.Presentation.Branding!.DisplayName);
    }

    [Fact]
    public async Task TenantWithNoPresentation_ResolvesToBuiltInDefaults()
    {
        // Every tenant provisioned before this feature existed is in exactly this state.
        await using var db = CreateCentralDbContext();
        await db.Database.MigrateAsync();

        var tenant = NewTenant("legacy");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await using var read = CreateCentralDbContext();
        var found = await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);

        Assert.Null(found.Presentation);

        var presentation = TenantPresentation.From(found.Presentation);
        Assert.Equal("Leads", presentation.Terminology.Plural(TerminologyTerm.Lead));
        Assert.Equal("308,000", presentation.Formatting.Amount(308_000m));
    }

    [Fact]
    public async Task InPlaceEdit_IsDetectedByChangeTracking()
    {
        // Without a value comparer EF compares the POCO by reference, so mutating a nested
        // property is invisible and the update is silently dropped — the tenant saves a
        // rename and nothing happens.
        await using var db = CreateCentralDbContext();
        await db.Database.MigrateAsync();

        var tenant = NewTenant("mutate");
        tenant.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = new TenantTerminology { Lead = new TermOverride { Singular = "Enquiry" } }
        });
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await using var edit = CreateCentralDbContext();
        var tracked = await edit.Tenants.SingleAsync(t => t.Id == tenant.Id);
        tracked.Presentation!.Terminology!.Lead!.Singular = "Referral";
        await edit.SaveChangesAsync();

        await using var read = CreateCentralDbContext();
        var found = await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Referral", found.Presentation!.Terminology!.Lead!.Singular);
    }

    [Fact]
    public async Task OneTenantsSettingsDoNotAffectAnother()
    {
        await using var db = CreateCentralDbContext();
        await db.Database.MigrateAsync();

        var dealership = NewTenant("dealer");
        dealership.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = new TenantTerminology { Lead = new TermOverride { Singular = "Enquiry", Plural = "Enquiries" } },
            Locale = new TenantLocale { CurrencySymbol = "£" }
        });

        var university = NewTenant("university");
        university.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = new TenantTerminology { Lead = new TermOverride { Singular = "Applicant", Plural = "Applicants" } },
            Locale = new TenantLocale { CurrencySymbol = "₹" }
        });

        var untouched = NewTenant("plain");

        db.Tenants.AddRange(dealership, university, untouched);
        await db.SaveChangesAsync();

        await using var read = CreateCentralDbContext();

        var d = TenantPresentation.From(
            (await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == dealership.Id)).Presentation);
        var u = TenantPresentation.From(
            (await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == university.Id)).Presentation);
        var p = TenantPresentation.From(
            (await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == untouched.Id)).Presentation);

        Assert.Equal("Enquiries", d.Terminology.Plural(TerminologyTerm.Lead));
        Assert.Equal("Applicants", u.Terminology.Plural(TerminologyTerm.Lead));
        Assert.Equal("Leads", p.Terminology.Plural(TerminologyTerm.Lead));

        Assert.Equal("£1,000", d.Formatting.Amount(1000m));
        Assert.Equal("₹1,000", u.Formatting.Amount(1000m));
        Assert.Equal("1,000", p.Formatting.Amount(1000m));
    }

    [Fact]
    public async Task ClearingPresentation_RestoresDefaults()
    {
        await using var db = CreateCentralDbContext();
        await db.Database.MigrateAsync();

        var tenant = NewTenant("cleared");
        tenant.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = new TenantTerminology { Lead = new TermOverride { Singular = "Enquiry" } }
        });
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await using var edit = CreateCentralDbContext();
        var tracked = await edit.Tenants.SingleAsync(t => t.Id == tenant.Id);
        tracked.UpdatePresentation(null);
        await edit.SaveChangesAsync();

        await using var read = CreateCentralDbContext();
        var found = await read.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);

        Assert.Null(found.Presentation);
        Assert.Equal("Lead", TenantPresentation.From(found.Presentation).Terminology.Singular(TerminologyTerm.Lead));
    }
}
