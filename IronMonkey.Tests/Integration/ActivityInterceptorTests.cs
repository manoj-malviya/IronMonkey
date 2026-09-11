using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Interceptors;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The existing timeline tests insert ActivityLog rows by hand, so they passed even though
/// nothing populated the table: the interceptor was registered but every call site used the
/// interceptor-less factory overload. These tests exercise the real write path instead.
/// </summary>
[Collection("Integration")]
public class ActivityInterceptorTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    /// <summary>Builds a factory wired the way the API wires it — interceptor attached.</summary>
    private static TenantDbContextFactory TrackingFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        var provider = services.BuildServiceProvider();

        return new TenantDbContextFactory(
            new IInterceptor[] { new ActivityChangeInterceptor(provider) });
    }

    private async Task<(string ConnectionString, Guid TenantId, Guid StageId)> SetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ai_{suffix}");

        await using var db = new TenantDbContextFactory().CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        return (connStr, tenantId, stage.Id);
    }

    [Fact]
    public async Task Creating_a_lead_writes_an_activity_row()
    {
        var (connStr, tenantId, stageId) = await SetupAsync();
        var factory = TrackingFactory();

        Guid leadId;
        await using (var db = factory.CreateForTenant(connStr, tenantId))
        {
            var lead = Lead.Create(tenantId, "Ada", "Lovelace", "555", "ada@test.com", LeadSource.Manual, stageId);
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            leadId = lead.Id;
        }

        await using var verify = new TenantDbContextFactory().CreateForTenant(connStr, tenantId);
        var log = await verify.ActivityLogs.SingleAsync(a => a.SubjectId == leadId);

        Assert.Equal("Created", log.EventType);
        Assert.Equal("Lead", log.SubjectType);
        Assert.Equal(leadId, log.LeadId);
        // A row with a default timestamp sorts to the bottom of every timeline forever.
        Assert.True(log.CreatedAt > DateTime.UtcNow.AddMinutes(-5), "CreatedAt was not stamped");
    }

    [Fact]
    public async Task Updating_an_opportunity_records_the_changed_fields()
    {
        var (connStr, tenantId, _) = await SetupAsync();
        var factory = TrackingFactory();

        Guid opportunityId;
        await using (var db = factory.CreateForTenant(connStr, tenantId))
        {
            var contact = Contact.Create(tenantId, "Grace Hopper", "555", "grace@test.com");
            db.Contacts.Add(contact);
            var opportunity = Opportunity.Create(tenantId, "Compiler licence", contact.Id, DateTime.UtcNow.AddDays(30), "Qualification");
            opportunity.SetAmount(1000m);
            db.Opportunities.Add(opportunity);
            await db.SaveChangesAsync();
            opportunityId = opportunity.Id;
        }

        await using (var db = factory.CreateForTenant(connStr, tenantId))
        {
            var opportunity = await db.Opportunities.SingleAsync(o => o.Id == opportunityId);
            opportunity.UpdateStage("Won");
            opportunity.SetAmount(2500m);
            await db.SaveChangesAsync();
        }

        await using var verify = new TenantDbContextFactory().CreateForTenant(connStr, tenantId);
        var update = await verify.ActivityLogs
            .Where(a => a.SubjectType == "Opportunity" && a.SubjectId == opportunityId && a.EventType == "Updated")
            .SingleAsync();

        Assert.Equal("Qualification", update.OldValues!["Stage"]!.ToString());
        Assert.Equal("Won", update.NewValues!["Stage"]!.ToString());
        Assert.Equal("1000", update.OldValues["Amount"]!.ToString());
        Assert.Equal("2500", update.NewValues["Amount"]!.ToString());

        // Opportunities are not lead-scoped, so LeadId stays null — this is exactly what the
        // old required FK made impossible.
        Assert.Null(update.LeadId);
    }

    [Fact]
    public async Task Contact_changes_land_on_the_contact_timeline()
    {
        var (connStr, tenantId, _) = await SetupAsync();
        var factory = TrackingFactory();

        Guid contactId;
        await using (var db = factory.CreateForTenant(connStr, tenantId))
        {
            var contact = Contact.Create(tenantId, "Alan Turing", "555", "alan@test.com");
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            contactId = contact.Id;
        }

        await using var verify = new TenantDbContextFactory().CreateForTenant(connStr, tenantId);
        var log = await verify.ActivityLogs.SingleAsync(a => a.SubjectId == contactId);

        Assert.Equal("Contact", log.SubjectType);
        Assert.Null(log.LeadId);
        // No HTTP context in a test, so the actor is "system" rather than a user.
        Assert.Null(log.ActorId);
    }
}
