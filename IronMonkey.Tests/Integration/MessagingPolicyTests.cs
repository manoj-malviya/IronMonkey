using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Quiet hours and rate limiting, enforced through the dispatcher.
///
/// The two refusals are deliberately different in kind: a rate limit is a hard refusal
/// (Rejected), while quiet hours are a <em>deferral</em> — the tenant asked for the message to
/// go, just not at 3am, so dropping it would lose a message rather than delay it.
/// </summary>
[Collection("Integration")]
public class MessagingPolicyTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class StubProvider : IMessageProvider
    {
        public MessageChannel Channel => MessageChannel.Sms;
        public string Name => "Fake";
        public bool IsConfigured => true;
        public string FromAddress => "+15550100000";

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
            => Task.FromResult(SendResult.Success("pid"));
    }

    private sealed class RecordingScheduler : IMessageSendScheduler
    {
        public List<Guid> Enqueued { get; } = [];
        public List<(Guid MessageId, DateTime RunAtUtc)> Scheduled { get; } = [];
        public void Enqueue(Guid tenantId, Guid messageId) => Enqueued.Add(messageId);
        public void Schedule(Guid tenantId, Guid messageId, DateTime runAtUtc) => Scheduled.Add((messageId, runAtUtc));
    }

    /// <summary>A fixed clock, so "is it quiet now" is a decision rather than a race.</summary>
    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private CentralDbContext CreateCentralDb()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options;
        return new CentralDbContext(options);
    }

    private async Task<(TenantDbContext Db, Guid TenantId)> NewTenantAsync(string? timeZoneId = null)
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var db = _factory.CreateForTenant(
            fixture.ConnectionString.Replace("ironmonkey_test", $"policy_{suffix}"), tenantId);
        await db.Database.MigrateAsync();

        // The tenant row carries the timezone quiet hours are judged in.
        await using var central = CreateCentralDb();
        await central.Database.MigrateAsync();

        var tenant = Tenant.Create($"policy-{suffix}", $"policy-{suffix}", "Standard", "Active");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);

        if (timeZoneId is not null)
        {
            tenant.UpdatePresentation(new TenantPresentationSettings
            {
                Locale = new TenantLocale { TimeZoneId = timeZoneId }
            });
        }

        central.Tenants.Add(tenant);
        await central.SaveChangesAsync();

        return (db, tenantId);
    }

    private MessageDispatcher CreateDispatcher(IMessageSendScheduler scheduler, DateTime now)
    {
        var clock = new FixedClock(now);

        return new MessageDispatcher(
            new MessageProviderRegistry([new StubProvider()]),
            scheduler,
            new SuppressionService(NullLogger<SuppressionService>.Instance, clock),
            clock,
            NullLogger<MessageDispatcher>.Instance,
            new MessagingPolicyService(CreateCentralDb(), clock,
                NullLogger<MessagingPolicyService>.Instance));
    }

    private static SendMessageCommand Command(Guid tenantId, string key) =>
        new(tenantId, MessageChannel.Sms, "+15550109999", null, "Body", key);

    [Fact]
    public async Task With_no_policy_row_a_tenant_is_unrestricted()
    {
        // Absence must read as "no restrictions", or adding this feature would silently block
        // every tenant provisioned before it existed.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc));

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "k1"), default);

        Assert.True(outcome.Queued);
        Assert.Single(scheduler.Enqueued);
        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task A_send_inside_quiet_hours_is_deferred_rather_than_dropped()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetQuietHours(new TimeOnly(21, 0), new TimeOnly(8, 0), [MessageChannel.Sms]);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc));

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "quiet"), default);

        // Still Queued — the message survives and goes out when the window closes.
        Assert.Equal(MessageStatus.Queued, outcome.Status);
        Assert.Empty(scheduler.Enqueued);
        Assert.Single(scheduler.Scheduled);

        var (_, runAt) = scheduler.Scheduled[0];
        Assert.Equal(new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc), runAt);
    }

    [Fact]
    public async Task A_send_outside_quiet_hours_goes_immediately()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetQuietHours(new TimeOnly(21, 0), new TimeOnly(8, 0), [MessageChannel.Sms]);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        await dispatcher.QueueAsync(db, Command(tenantId, "daytime"), default);

        Assert.Single(scheduler.Enqueued);
        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task Quiet_hours_are_judged_in_the_tenants_timezone()
    {
        // 18:00 UTC is 23:30 in Kolkata — quiet there, and the middle of the working day in
        // UTC. A server judging this in its own zone would message people overnight.
        var (db, tenantId) = await NewTenantAsync("Asia/Kolkata");
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetQuietHours(new TimeOnly(21, 0), new TimeOnly(8, 0), [MessageChannel.Sms]);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, new DateTime(2026, 3, 10, 18, 0, 0, DateTimeKind.Utc));

        await dispatcher.QueueAsync(db, Command(tenantId, "kolkata"), default);

        Assert.Single(scheduler.Scheduled);
        Assert.Empty(scheduler.Enqueued);
    }

    [Fact]
    public async Task A_channel_outside_the_window_is_unaffected()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        // Quiet hours apply to email only; the SMS send should go straight out.
        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetQuietHours(new TimeOnly(21, 0), new TimeOnly(8, 0), [MessageChannel.Email]);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc));

        await dispatcher.QueueAsync(db, Command(tenantId, "sms-ok"), default);

        Assert.Single(scheduler.Enqueued);
        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task The_rate_limit_rejects_rather_than_defers()
    {
        // Unlike quiet hours, there is no known time at which a rate-limited send becomes
        // acceptable, so it is refused and recorded rather than scheduled indefinitely.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetRateLimit(2);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var scheduler = new RecordingScheduler();
        var now = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var dispatcher = CreateDispatcher(scheduler, now);

        Assert.True((await dispatcher.QueueAsync(db, Command(tenantId, "r1"), default)).Queued);
        Assert.True((await dispatcher.QueueAsync(db, Command(tenantId, "r2"), default)).Queued);

        var third = await dispatcher.QueueAsync(db, Command(tenantId, "r3"), default);

        Assert.Equal(MessageStatus.Rejected, third.Status);
        Assert.Equal(MessageErrorCategory.RateLimited, third.ErrorCategory);
        Assert.Equal(2, scheduler.Enqueued.Count);
    }

    [Fact]
    public async Task A_rejected_message_does_not_consume_rate_limit_capacity()
    {
        // Rejections are recorded as message rows. If they counted, each refusal would push
        // the count higher and the window could never drain — the limit would latch on
        // permanently after the first breach.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetRateLimit(2);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var now = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        // Fill the window, then breach it twice.
        var dispatcher = CreateDispatcher(new RecordingScheduler(), now);
        await dispatcher.QueueAsync(db, Command(tenantId, "c1"), default);
        await dispatcher.QueueAsync(db, Command(tenantId, "c2"), default);
        await dispatcher.QueueAsync(db, Command(tenantId, "c3"), default);
        await dispatcher.QueueAsync(db, Command(tenantId, "c4"), default);

        var rejected = await db.Messages.CountAsync(m => m.Status == MessageStatus.Rejected);
        Assert.Equal(2, rejected);

        // Well after the two real sends have aged out, but with the rejections still inside the
        // window. Capacity must have been restored.
        var scheduler = new RecordingScheduler();
        var outcome = await CreateDispatcher(scheduler, now.AddHours(2))
            .QueueAsync(db, Command(tenantId, "c5"), default);

        Assert.True(outcome.Queued);
        Assert.Single(scheduler.Enqueued);
    }

    [Fact]
    public async Task Messages_older_than_an_hour_do_not_count_toward_the_limit()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var policy = MessagingPolicy.CreateDefault(tenantId);
        policy.SetRateLimit(1);
        db.MessagingPolicies.Add(policy);
        await db.SaveChangesAsync();

        var earlier = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc);
        await CreateDispatcher(new RecordingScheduler(), earlier)
            .QueueAsync(db, Command(tenantId, "old"), default);

        // Two hours later the window has moved past the first message.
        var later = earlier.AddHours(2);
        var scheduler = new RecordingScheduler();

        var outcome = await CreateDispatcher(scheduler, later)
            .QueueAsync(db, Command(tenantId, "new"), default);

        Assert.True(outcome.Queued);
        Assert.Single(scheduler.Enqueued);
    }

    [Fact]
    public void A_half_specified_quiet_window_is_treated_as_unset()
    {
        // One end alone would otherwise be an open-ended window that silences the tenant
        // indefinitely.
        var policy = MessagingPolicy.CreateDefault(Guid.NewGuid());

        policy.SetQuietHours(new TimeOnly(21, 0), null, [MessageChannel.Sms]);
        Assert.Null(policy.ToWindow());

        policy.SetQuietHours(null, new TimeOnly(8, 0), [MessageChannel.Sms]);
        Assert.Null(policy.ToWindow());
    }

    [Fact]
    public void A_zero_or_negative_rate_limit_means_no_limit()
    {
        var policy = MessagingPolicy.CreateDefault(Guid.NewGuid());

        policy.SetRateLimit(0);
        Assert.Null(policy.MaxMessagesPerHour);

        policy.SetRateLimit(-5);
        Assert.Null(policy.MaxMessagesPerHour);

        policy.SetRateLimit(10);
        Assert.Equal(10, policy.MaxMessagesPerHour);
    }

    [Fact]
    public void An_unparseable_stored_channel_is_skipped_rather_than_throwing()
    {
        // A renamed channel must not make the whole policy unreadable and silently drop quiet
        // hours for every channel.
        var policy = MessagingPolicy.CreateDefault(Guid.NewGuid());
        policy.SetQuietHours(new TimeOnly(21, 0), new TimeOnly(8, 0), [MessageChannel.Sms]);

        typeof(MessagingPolicy)
            .GetProperty(nameof(MessagingPolicy.QuietHoursChannels))!
            .SetValue(policy, "Sms,Telepathy");

        var window = policy.ToWindow();

        Assert.NotNull(window);
        Assert.Equal([MessageChannel.Sms], window!.Channels);
    }
}
