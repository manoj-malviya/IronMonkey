using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-02: Dashboard shows conversion rates by stage, source, and time period
[Collection("Integration")]
public class ConversionDashboardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetConversionDashboard_CalculatesConversionRateByStage() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetConversionDashboard_CalculatesConversionRateByLeadSource() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetConversionDashboard_FiltersByPresetTimePeriod_ThisMonth() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetConversionDashboard_FiltersByCustomDateRange() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetConversionDashboard_DefaultPeriodIsThisMonth() => await Task.CompletedTask;
}
