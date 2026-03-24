using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// ACTV-01: Unified activity timeline for a lead — paginated, filterable, newest first
[Collection("Integration")]
public class ActivityTimelineTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task GetTimeline_ReturnsEventsNewestFirst() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task GetTimeline_PageSize20_HasMoreTrueWhenMoreExist() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task GetTimeline_FilterByEventType_ReturnsOnlyMatchingEvents() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task AddNote_CreatesActivityLogEntryWithNoteContent() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task GetTimeline_LeadWithNoEvents_ReturnsEmptyList() => await Task.CompletedTask;
}
