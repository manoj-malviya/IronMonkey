using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the paging, sorting and counting contract the lead list depends on: the page
/// must be a stable window over a filtered total, not a prefix of an arbitrary ordering.
/// </summary>
[Collection("Integration")]
public class LeadListPaginationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);
    }

    private async Task<(string ConnectionString, Guid StageId)> SetupAsync(Guid tenantId, string label, int leadCount)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"page_{label}_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        for (var i = 0; i < leadCount; i++)
        {
            db.Leads.Add(Lead.Create(
                tenantId, $"Lead{i:D3}", "Test", $"555{i:D4}", $"lead{i:D3}@t.com",
                LeadSource.Manual, stage.Id));
        }
        await db.SaveChangesAsync();

        return (connectionString, stage.Id);
    }

    /// <summary>
    /// Calls the list handler with no pipeline id, which resolves to the tenant's default —
    /// for a single-pipeline tenant, the only one there is. These tests therefore assert the
    /// unchanged single-pipeline behaviour, which is exactly what they were written for.
    /// </summary>
    private static async Task<ListLeadsEndpoint.LeadPage> Fetch(
        Guid tenantId, string connectionString, TenantDbContextFactory factory,
        string? search = null, Guid? stageId = null, string? sort = null,
        int? page = null, int? pageSize = null)
        => (await ListLeadsEndpoint.Handle(
            search, stageId, sort, page, pageSize, null,
            new FixedTenantService(tenantId, connectionString), factory,
            PipelineTestHelpers.Scope(), CancellationToken.None)).Ok();

    [Fact]
    public async Task ReturnsFirstPage_WithTotalReflectingEveryMatch()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "first", 30);

        var result = await Fetch(tenantId, connectionString, _factory, pageSize: 10);

        Assert.Equal(10, result.Items.Count);

        // The total counts every match, not the page — otherwise "1–10 of 10" would be
        // shown for a 30-row result.
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task PagesDoNotOverlapOrSkipRows()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "window", 25);

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Fetch(tenantId, connectionString, _factory, page: page, pageSize: 10);
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        // Every lead exactly once: a sort without a unique tiebreak would let rows repeat
        // across pages and others never appear.
        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Distinct().Count());
    }

    [Fact]
    public async Task PageBeyondTheEnd_ClampsToTheLastPage()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "clamp", 12);

        var result = await Fetch(tenantId, connectionString, _factory, page: 99, pageSize: 10);

        // A stale deep link returns the last real page rather than an empty list that
        // would read as "no leads".
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(12, result.TotalCount);
    }

    [Fact]
    public async Task PageSize_IsCappedSoCallersCannotRequestEverything()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "cap", 40);

        var result = await Fetch(tenantId, connectionString, _factory, pageSize: 100_000);

        Assert.True(result.PageSize <= 200);
        Assert.Equal(40, result.TotalCount);
    }

    [Fact]
    public async Task SearchNarrowsTheTotal_NotJustThePage()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "search", 30);

        // Matches exactly one seeded lead.
        var result = await Fetch(tenantId, connectionString, _factory, search: "Lead007");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal("Lead007", result.Items.Single().FirstName);
    }

    [Fact]
    public async Task SortByName_OrdersAscendingAndDescending()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "sort", 15);

        var ascending = await Fetch(tenantId, connectionString, _factory, sort: "name", pageSize: 5);
        var descending = await Fetch(tenantId, connectionString, _factory, sort: "name_desc", pageSize: 5);

        Assert.Equal("Lead000", ascending.Items.First().FirstName);
        Assert.Equal("Lead014", descending.Items.First().FirstName);
    }

    [Fact]
    public async Task UnknownSortKey_FallsBackToNewestFirstRatherThanThrowing()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "badsort", 5);

        // An unrecognized key must not reach the database as a property name.
        var result = await Fetch(tenantId, connectionString, _factory, sort: "'; DROP TABLE leads;--");

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task EmptyTenant_ReportsZeroPages()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantId, "none", 0);

        var result = await Fetch(tenantId, connectionString, _factory);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);

        // Still page 1, so the UI has a coherent "page 1 of 0" rather than page 0.
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task DoesNotPageOverAnotherTenantsLeads()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connectionString, _) = await SetupAsync(tenantA, "iso", 3);

        await using (var dbB = _factory.CreateForTenant(connectionString, tenantB))
        {
            var stage = PipelineStage.Create(tenantB, "New", 1, StageType.Entry);
            dbB.PipelineStages.Add(stage);
            await dbB.SaveChangesAsync();

            for (var i = 0; i < 10; i++)
            {
                dbB.Leads.Add(Lead.Create(
                    tenantB, $"Other{i}", "Tenant", $"9{i}", $"other{i}@t.com",
                    LeadSource.Manual, stage.Id));
            }
            await dbB.SaveChangesAsync();
        }

        var result = await Fetch(tenantA, connectionString, _factory);

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, i => Assert.StartsWith("Lead", i.FirstName));
    }
}
