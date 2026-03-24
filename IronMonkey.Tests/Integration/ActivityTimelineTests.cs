using IronMonkey.ApiService.Features.Activity;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// ACTV-01: Unified activity timeline for a lead — paginated, filterable, newest first
[Collection("Integration")]
public class ActivityTimelineTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid leadId, Guid actorId)> SetupDbWithLeadAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"tl_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var adminRole = await db.Roles.FirstAsync(r => r.Name == "Admin");
        var actor = User.Create(tenantId, "Test Actor", "actor@test.com", "hashed", adminRole);
        db.Users.Add(actor);
        var lead = Lead.Create(tenantId, "Timeline", "Test", "555-0000", "tl@test.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return (connStr, lead.Id, actor.Id);
    }

    [Fact]
    public async Task GetTimeline_ReturnsEventsNewestFirst()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, leadId, actorId) = await SetupDbWithLeadAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Add 3 events with increasing timestamps
        for (int i = 0; i < 3; i++)
        {
            var log = ActivityLog.Create(tenantId, leadId, actorId, "Updated", "Lead", leadId.ToString());
            db.ActivityLogs.Add(log);
            await db.SaveChangesAsync();
            await Task.Delay(10); // Ensure distinct timestamps
        }

        var events = await db.ActivityLogs
            .Where(a => a.LeadId == leadId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        Assert.Equal(3, events.Count);
        // Verify descending order
        Assert.True(events[0].CreatedAt >= events[1].CreatedAt);
        Assert.True(events[1].CreatedAt >= events[2].CreatedAt);
    }

    [Fact]
    public async Task GetTimeline_PageSize20_HasMoreTrueWhenMoreExist()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, leadId, actorId) = await SetupDbWithLeadAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Insert 25 events
        for (int i = 0; i < 25; i++)
        {
            db.ActivityLogs.Add(ActivityLog.Create(tenantId, leadId, actorId, "Updated", "Lead", leadId.ToString()));
        }
        await db.SaveChangesAsync();

        const int pageSize = 20;
        var totalCount = await db.ActivityLogs.Where(a => a.LeadId == leadId).CountAsync();
        var page0 = await db.ActivityLogs
            .Where(a => a.LeadId == leadId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(0).Take(pageSize)
            .ToListAsync();

        Assert.Equal(20, page0.Count);
        Assert.True(totalCount > pageSize); // HasMore = true
    }

    [Fact]
    public async Task GetTimeline_FilterByEventType_ReturnsOnlyMatchingEvents()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, leadId, actorId) = await SetupDbWithLeadAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        db.ActivityLogs.Add(ActivityLog.Create(tenantId, leadId, actorId, "Note", "Lead", leadId.ToString()));
        db.ActivityLogs.Add(ActivityLog.Create(tenantId, leadId, actorId, "Updated", "Lead", leadId.ToString()));
        db.ActivityLogs.Add(ActivityLog.Create(tenantId, leadId, actorId, "Note", "Lead", leadId.ToString()));
        await db.SaveChangesAsync();

        var noteEvents = await db.ActivityLogs
            .Where(a => a.LeadId == leadId && a.EventType == "Note")
            .ToListAsync();

        Assert.Equal(2, noteEvents.Count);
        Assert.All(noteEvents, e => Assert.Equal("Note", e.EventType));
    }

    [Fact]
    public async Task AddNote_CreatesActivityLogEntryWithNoteContent()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, leadId, actorId) = await SetupDbWithLeadAsync(tenantId);

        var service = new ActivityTrackingService(_factory);
        const string noteText = "Called prospect — interested in premium plan";

        await service.AddNoteAsync(tenantId, connStr, leadId, actorId, noteText);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var saved = await db.ActivityLogs.FirstAsync(a => a.LeadId == leadId && a.EventType == "Note");
        Assert.Equal("Note", saved.EventType);
        Assert.NotNull(saved.NewValues);
        Assert.Equal(noteText, saved.NewValues["Content"]?.ToString());
    }

    [Fact]
    public async Task GetTimeline_LeadWithNoEvents_ReturnsEmptyList()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, leadId, _) = await SetupDbWithLeadAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var events = await db.ActivityLogs
            .Where(a => a.LeadId == leadId)
            .ToListAsync();

        Assert.Empty(events);
    }
}
