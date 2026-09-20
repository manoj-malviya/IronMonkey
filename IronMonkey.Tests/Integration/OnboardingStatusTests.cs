using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Onboarding;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the tenant setup-status contract by invoking the real handlers, so the completion
/// rules under test are the rules that ship rather than a copy rewritten here.
///
/// The cases that matter are the boundaries: a freshly provisioned tenant (empty), a tenant
/// part-way through (partial), a fully configured one (complete), a caller with no tenant
/// (unauthorized), and one tenant's setup being invisible to another (cross-tenant).
/// </summary>
[Collection("Integration")]
public class OnboardingStatusTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    /// <summary>
    /// Stands in for request-scoped tenant resolution, exactly as the dashboard tests do. The
    /// real ITenantService reads the tenant from the JWT; pinning it here is what lets a test
    /// assert that a handler given tenant A's identity cannot observe tenant B's rows.
    /// </summary>
    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);
    }

    /// <summary>
    /// Stands in for the claims-derived user context. UserId is User.Id — the value
    /// LoginEndpoint puts in the identity claim — never IdentityId, which is "" for every
    /// tenant user.
    /// </summary>
    private sealed class FixedUserContext(Guid userId, Guid tenantId) : IUserContext
    {
        public Guid UserId => userId;
        public string IdentityId => string.Empty;
        public Guid TenantId => tenantId;
        public string? ActAs => null;
    }

    private CentralDbContext CentralDb()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    private async Task<string> CreateTenantDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"onb_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private TenantDbContext Db(string connectionString, Guid tenantId)
        => _factory.CreateForTenant(connectionString, tenantId);

    /// <summary>
    /// Registers the tenant in the central database so the context strip has a real name to
    /// read, mirroring what provisioning does.
    /// </summary>
    private async Task<string> RegisterTenantAsync(Guid tenantId, string name)
    {
        await using var central = CentralDb();
        await central.Database.MigrateAsync();

        var tenant = Tenant.Create(name, $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}", "Standard", "Active");

        // Tenant.Id is init-only and assigned by the factory, so the row is inserted with the
        // id the handler will look up by setting it through the tracked entry.
        var entry = central.Tenants.Add(tenant);
        entry.Property(t => t.Id).CurrentValue = tenantId;

        await central.SaveChangesAsync();
        return name;
    }

    /// <summary>Creates a tenant user with the Admin role and returns its id.</summary>
    private static async Task<Guid> AddUserAsync(TenantDbContext db, Guid tenantId, string name, string email)
    {
        var role = await db.Roles.SingleAsync(r => r.Name == "Admin");
        var user = User.Create(tenantId, name, email, "hashed", role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private Task<OnboardingStatusResponse> StatusAsync(
        TenantDbContext db, Guid tenantId, Guid userId)
    {
        var central = CentralDb();
        return OnboardingStatusReader.ReadAsync(db, central, tenantId, userId, CancellationToken.None);
    }

    // ── Empty: a newly provisioned tenant ────────────────────────────────────────────

    [Fact]
    public async Task EmptyTenant_ReportsFourIncompleteSteps_AndDoesNotThrow()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Northgate");
        var cs = await CreateTenantDbAsync(tenantId, "empty");
        await using var db = Db(cs, tenantId);

        // Only the admin created at provisioning. Nothing else configured at all.
        var adminId = await AddUserAsync(db, tenantId, "Ada Admin", "ada@northgate.test");

        var status = await StatusAsync(db, tenantId, adminId);

        Assert.Equal(4, status.TotalCount);
        Assert.Equal(0, status.CompletedCount);
        Assert.Equal(0, status.CompletionPercentage);
        Assert.False(status.IsComplete);
        Assert.All(status.Steps, s => Assert.False(s.IsComplete));

        // Every incomplete step must carry something useful to show, or the empty state is a
        // row of blank bullets.
        Assert.All(status.Steps, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.EmptyStateHint));
            Assert.False(string.IsNullOrWhiteSpace(s.Link));
            Assert.False(string.IsNullOrWhiteSpace(s.LinkText));
        });

        // Not dismissed by anyone yet, so the dashboard shows it.
        Assert.False(status.IsDismissed);
        Assert.True(status.ShouldShowChecklist);
    }

    [Fact]
    public async Task EmptyTenant_TenantContext_NamesTheTenantAndTheCurrentUser()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Northgate");
        var cs = await CreateTenantDbAsync(tenantId, "ctx");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada Admin", "ada@northgate.test");

        var status = await StatusAsync(db, tenantId, adminId);

        Assert.Equal(tenantId, status.Tenant.TenantId);
        Assert.Equal("Northgate", status.Tenant.TenantName);
        Assert.Equal("Ada Admin", status.Tenant.UserName);
        Assert.Equal("ada@northgate.test", status.Tenant.UserEmail);
        Assert.Contains("Admin", status.Tenant.RoleName);
        Assert.False(string.IsNullOrWhiteSpace(status.Tenant.SettingsLink));
    }

    /// <summary>
    /// The handler must survive a tenant database with literally nothing in it — no users at
    /// all, not even the admin. A null-dereference here would 500 the dashboard panel for a
    /// tenant mid-provisioning.
    /// </summary>
    [Fact]
    public async Task TenantWithNoUsersAtAll_IsSafe_AndFallsBackRatherThanThrowing()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Ghost");
        var cs = await CreateTenantDbAsync(tenantId, "noone");
        await using var db = Db(cs, tenantId);

        var status = await StatusAsync(db, tenantId, Guid.NewGuid());

        Assert.Equal(0, status.CompletedCount);
        Assert.False(string.IsNullOrWhiteSpace(status.Tenant.UserName));
        Assert.False(string.IsNullOrWhiteSpace(status.Tenant.RoleName));
    }

    // ── Partial: steps complete one at a time ────────────────────────────────────────

    [Fact]
    public async Task AddingATeamMember_CompletesOnlyThatStep()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Partial");
        var cs = await CreateTenantDbAsync(tenantId, "team");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada Admin", "ada@p.test");

        var before = await StatusAsync(db, tenantId, adminId);
        Assert.False(Step(before, OnboardingStepKeys.TeamMember).IsComplete);

        await AddUserAsync(db, tenantId, "Ben Member", "ben@p.test");

        var after = await StatusAsync(db, tenantId, adminId);

        Assert.True(Step(after, OnboardingStepKeys.TeamMember).IsComplete);
        Assert.Equal(1, after.CompletedCount);
        Assert.Equal(25, after.CompletionPercentage);

        // The other three must not have moved.
        Assert.False(Step(after, OnboardingStepKeys.PipelineStages).IsComplete);
        Assert.False(Step(after, OnboardingStepKeys.CustomField).IsComplete);
        Assert.False(Step(after, OnboardingStepKeys.LeadRouting).IsComplete);
    }

    /// <summary>
    /// The lone admin created at provisioning is not "an additional team member". Counting
    /// one user would tick this step for every tenant the moment it was provisioned.
    /// </summary>
    [Fact]
    public async Task SoleAdmin_DoesNotCountAsATeamMember()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Solo");
        var cs = await CreateTenantDbAsync(tenantId, "solo");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada Admin", "ada@solo.test");

        var status = await StatusAsync(db, tenantId, adminId);
        Assert.False(Step(status, OnboardingStepKeys.TeamMember).IsComplete);
    }

    /// <summary>
    /// A single stage is not a pipeline — a lead has nowhere to move to — so one stage must
    /// leave the step outstanding.
    /// </summary>
    [Fact]
    public async Task OneStage_IsNotAConfiguredPipeline_ButTwoAre()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Stages");
        var cs = await CreateTenantDbAsync(tenantId, "stages");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@s.test");

        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var one = await StatusAsync(db, tenantId, adminId);
        Assert.False(Step(one, OnboardingStepKeys.PipelineStages).IsComplete);

        db.PipelineStages.Add(PipelineStage.Create(tenantId, "Won", 2, StageType.ClosedWon));
        await db.SaveChangesAsync();

        var two = await StatusAsync(db, tenantId, adminId);
        Assert.True(Step(two, OnboardingStepKeys.PipelineStages).IsComplete);
    }

    /// <summary>
    /// An archived field renders on no form, so a tenant whose only field is archived is
    /// capturing nothing custom and has not completed the step.
    /// </summary>
    [Fact]
    public async Task ArchivedCustomField_DoesNotCompleteTheStep()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Fields");
        var cs = await CreateTenantDbAsync(tenantId, "fields");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@f.test");

        var field = CustomFieldDefinition.Create(tenantId, "Budget", CustomFieldType.Number, false);
        db.CustomFieldDefinitions.Add(field);
        await db.SaveChangesAsync();

        var active = await StatusAsync(db, tenantId, adminId);
        Assert.True(Step(active, OnboardingStepKeys.CustomField).IsComplete);

        field.Archive();
        await db.SaveChangesAsync();

        var archived = await StatusAsync(db, tenantId, adminId);
        Assert.False(Step(archived, OnboardingStepKeys.CustomField).IsComplete);
    }

    /// <summary>
    /// The routing step is "reviewed", not "enabled". A tenant that deliberately turned
    /// routing off has made the decision the step exists to prompt, so a disabled config
    /// still completes it.
    /// </summary>
    [Fact]
    public async Task DisabledRoutingConfig_StillCountsAsReviewed()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Routing");
        var cs = await CreateTenantDbAsync(tenantId, "routing");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@r.test");

        var before = await StatusAsync(db, tenantId, adminId);
        Assert.False(Step(before, OnboardingStepKeys.LeadRouting).IsComplete);

        var config = RoutingConfig.Create(tenantId, RoutingStrategy.RoundRobin, RoutingDimension.LeadSource);
        config.Disable();
        db.RoutingConfigs.Add(config);
        await db.SaveChangesAsync();

        var after = await StatusAsync(db, tenantId, adminId);
        Assert.True(Step(after, OnboardingStepKeys.LeadRouting).IsComplete);
    }

    // ── Complete ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullyConfiguredTenant_Is100Percent_AndTheChecklistStopsShowing()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Complete");
        var cs = await CreateTenantDbAsync(tenantId, "done");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@c.test");
        await AddUserAsync(db, tenantId, "Ben", "ben@c.test");

        db.PipelineStages.AddRange(
            PipelineStage.Create(tenantId, "New", 1, StageType.Entry),
            PipelineStage.Create(tenantId, "Won", 2, StageType.ClosedWon));
        db.CustomFieldDefinitions.Add(
            CustomFieldDefinition.Create(tenantId, "Budget", CustomFieldType.Number, false));
        db.RoutingConfigs.Add(
            RoutingConfig.Create(tenantId, RoutingStrategy.RoundRobin, RoutingDimension.LeadSource));
        await db.SaveChangesAsync();

        var status = await StatusAsync(db, tenantId, adminId);

        Assert.Equal(4, status.CompletedCount);
        Assert.Equal(100, status.CompletionPercentage);
        Assert.True(status.IsComplete);

        // Complete means the checklist goes away without anyone having to dismiss it — that
        // is what keeps it from competing with real CRM work.
        Assert.False(status.IsDismissed);
        Assert.False(status.ShouldShowChecklist);
    }

    /// <summary>
    /// Three of four must not round up to 100 and read as finished while an item is open.
    /// </summary>
    [Fact]
    public async Task ThreeOfFour_ReportsSeventyFive_NotOneHundred()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "ThreeOfFour");
        var cs = await CreateTenantDbAsync(tenantId, "three");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@t.test");
        await AddUserAsync(db, tenantId, "Ben", "ben@t.test");

        db.PipelineStages.AddRange(
            PipelineStage.Create(tenantId, "New", 1, StageType.Entry),
            PipelineStage.Create(tenantId, "Won", 2, StageType.ClosedWon));
        db.CustomFieldDefinitions.Add(
            CustomFieldDefinition.Create(tenantId, "Budget", CustomFieldType.Number, false));
        await db.SaveChangesAsync();

        var status = await StatusAsync(db, tenantId, adminId);

        Assert.Equal(3, status.CompletedCount);
        Assert.Equal(75, status.CompletionPercentage);
        Assert.False(status.IsComplete);
        Assert.True(status.ShouldShowChecklist);
    }

    // ── Dismissal ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dismissal_PersistsForThatUser_AndCanBeReopened()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Dismiss");
        var cs = await CreateTenantDbAsync(tenantId, "dismiss");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@d.test");

        var dismissed = await SetDismissalAsync(cs, tenantId, adminId, dismissed: true);
        Assert.True(dismissed.IsDismissed);
        Assert.False(dismissed.ShouldShowChecklist);

        // Persisted, not just in the response: a fresh context must still see it.
        await using var reread = Db(cs, tenantId);
        var afterReload = await StatusAsync(reread, tenantId, adminId);
        Assert.True(afterReload.IsDismissed);
        Assert.False(afterReload.ShouldShowChecklist);

        // Steps still outstanding, so reopening brings it back.
        var reopened = await SetDismissalAsync(cs, tenantId, adminId, dismissed: false);
        Assert.False(reopened.IsDismissed);
        Assert.True(reopened.ShouldShowChecklist);
    }

    /// <summary>
    /// A dismissal belongs to one user, not to the tenant. Keying it on anything shared —
    /// IdentityId, which is "" for every tenant user, being the obvious trap — would hide the
    /// checklist from colleagues who never dismissed it.
    /// </summary>
    [Fact]
    public async Task OneUsersDismissal_DoesNotHideTheChecklistFromAColleague()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "TwoUsers");
        var cs = await CreateTenantDbAsync(tenantId, "twouser");
        await using var db = Db(cs, tenantId);

        var adaId = await AddUserAsync(db, tenantId, "Ada", "ada@tu.test");
        var benId = await AddUserAsync(db, tenantId, "Ben", "ben@tu.test");

        await SetDismissalAsync(cs, tenantId, adaId, dismissed: true);

        await using var reread = Db(cs, tenantId);

        var ada = await StatusAsync(reread, tenantId, adaId);
        Assert.True(ada.IsDismissed);

        var ben = await StatusAsync(reread, tenantId, benId);
        Assert.False(ben.IsDismissed);
        Assert.True(ben.ShouldShowChecklist);
    }

    /// <summary>
    /// Dismissing twice must not violate the unique (TenantId, UserId) index — the second
    /// call updates the existing row rather than inserting a second one.
    /// </summary>
    [Fact]
    public async Task DismissingTwice_UpdatesInPlace_AndDoesNotViolateTheUniqueIndex()
    {
        var tenantId = Guid.NewGuid();
        await RegisterTenantAsync(tenantId, "Twice");
        var cs = await CreateTenantDbAsync(tenantId, "twice");
        await using var db = Db(cs, tenantId);

        var adminId = await AddUserAsync(db, tenantId, "Ada", "ada@tw.test");

        await SetDismissalAsync(cs, tenantId, adminId, dismissed: true);
        await SetDismissalAsync(cs, tenantId, adminId, dismissed: false);
        var third = await SetDismissalAsync(cs, tenantId, adminId, dismissed: true);

        Assert.True(third.IsDismissed);

        await using var reread = Db(cs, tenantId);
        Assert.Equal(1, await reread.TenantOnboardingDismissals.CountAsync());
    }

    // ── Cross-tenant ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tenant A's configuration and dismissals must be entirely invisible to tenant B, even
    /// when the two use the same user id — which is exactly what a leaked global filter or a
    /// missing TenantId predicate would expose.
    /// </summary>
    [Fact]
    public async Task OneTenantsSetup_IsInvisibleToAnother()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await RegisterTenantAsync(tenantA, "Alpha");
        await RegisterTenantAsync(tenantB, "Beta");

        var csA = await CreateTenantDbAsync(tenantA, "xta");
        var csB = await CreateTenantDbAsync(tenantB, "xtb");

        // The same user id in both tenants: a filter keyed only on UserId would cross over.
        var sharedUserId = Guid.NewGuid();

        await using (var dbA = Db(csA, tenantA))
        {
            var role = await dbA.Roles.SingleAsync(r => r.Name == "Admin");
            var ada = User.Create(tenantA, "Ada", "ada@alpha.test", "h", role);
            dbA.Users.Add(ada);
            dbA.Entry(ada).Property(u => u.Id).CurrentValue = sharedUserId;

            var second = User.Create(tenantA, "Ben", "ben@alpha.test", "h", role);
            dbA.Users.Add(second);

            dbA.PipelineStages.AddRange(
                PipelineStage.Create(tenantA, "New", 1, StageType.Entry),
                PipelineStage.Create(tenantA, "Won", 2, StageType.ClosedWon));
            dbA.CustomFieldDefinitions.Add(
                CustomFieldDefinition.Create(tenantA, "Budget", CustomFieldType.Number, false));
            dbA.RoutingConfigs.Add(
                RoutingConfig.Create(tenantA, RoutingStrategy.RoundRobin, RoutingDimension.LeadSource));
            await dbA.SaveChangesAsync();
        }

        await using (var dbB = Db(csB, tenantB))
        {
            var role = await dbB.Roles.SingleAsync(r => r.Name == "Admin");
            var solo = User.Create(tenantB, "Bea", "bea@beta.test", "h", role);
            dbB.Users.Add(solo);
            dbB.Entry(solo).Property(u => u.Id).CurrentValue = sharedUserId;
            await dbB.SaveChangesAsync();
        }

        // A dismisses; B must not inherit it.
        await SetDismissalAsync(csA, tenantA, sharedUserId, dismissed: true);

        await using var readA = Db(csA, tenantA);
        var statusA = await StatusAsync(readA, tenantA, sharedUserId);

        await using var readB = Db(csB, tenantB);
        var statusB = await StatusAsync(readB, tenantB, sharedUserId);

        // A is fully configured; B has configured nothing.
        Assert.Equal(4, statusA.CompletedCount);
        Assert.Equal(0, statusB.CompletedCount);

        // B must see its own name and its own user, never A's.
        Assert.Equal("Alpha", statusA.Tenant.TenantName);
        Assert.Equal("Beta", statusB.Tenant.TenantName);
        Assert.Equal("Bea", statusB.Tenant.UserName);

        // The dismissal is A's alone.
        Assert.True(statusA.IsDismissed);
        Assert.False(statusB.IsDismissed);
        Assert.True(statusB.ShouldShowChecklist);
    }

    // ── Unauthorized ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A platform SuperAdmin's token carries tenant_id = Guid.Empty, which TenantService
    /// rejects — so a tenant-scoped endpoint is unreachable for them by construction, exactly
    /// as on every other tenant endpoint. This pins that the onboarding handler goes through
    /// that same gate rather than quietly serving Guid.Empty as if it were a tenant.
    /// </summary>
    [Fact]
    public async Task PlatformCallerWithNoTenant_IsRejectedByTenantResolution()
    {
        var rejecting = new RejectingTenantService();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await GetOnboardingStatusEndpoint.Handle(
                rejecting,
                new FixedUserContext(Guid.NewGuid(), Guid.Empty),
                _factory,
                CentralDb(),
                CancellationToken.None));
    }

    /// <summary>Mirrors how the real TenantService refuses a Guid.Empty tenant claim.</summary>
    private sealed class RejectingTenantService : ITenantService
    {
        public Guid GetCurrentTenantId()
            => throw new UnauthorizedAccessException("No tenant context available.");

        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => throw new UnauthorizedAccessException("No tenant context available.");
    }

    /// <summary>
    /// The status endpoint is authorization-protected rather than anonymous. Pinned against
    /// the route metadata so a future refactor cannot quietly drop the requirement and expose
    /// a tenant's setup state — and its user's name and email — to an unauthenticated caller.
    /// </summary>
    [Fact]
    public void BothOnboardingEndpoints_RequireAuthorization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        // Minimal APIs resolve parameter binding at Map time: a handler parameter whose type
        // is not registered in DI is inferred as a request body, and the first such parameter
        // on a GET throws outright. Registering the handlers' dependencies mirrors what the
        // real ConfigureServices does, so the routes bind here exactly as they do in the app.
        builder.Services.AddScoped<ITenantService>(_ => new FixedTenantService(Guid.NewGuid(), string.Empty));
        builder.Services.AddScoped<IUserContext>(_ => new FixedUserContext(Guid.NewGuid(), Guid.NewGuid()));
        builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        builder.Services.AddDbContext<CentralDbContext>(o => o.UseNpgsql(fixture.ConnectionString));

        var app = builder.Build();

        GetOnboardingStatusEndpoint.Map(app);
        SetOnboardingDismissalEndpoint.Map(app);

        // Read the builder's own data sources rather than resolving EndpointDataSource from
        // DI: nothing populates the DI-registered source until the app is actually started,
        // so that route would report an empty table and the assertion would pass vacuously
        // for an endpoint that had no authorization at all.
        var onboarding = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(s => s.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/onboarding", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, onboarding.Count);

        Assert.All(onboarding, e =>
        {
            Assert.NotNull(e.Metadata
                .GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
            Assert.Null(e.Metadata
                .GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
        });
    }

    private async Task<OnboardingStatusResponse> SetDismissalAsync(
        string connectionString, Guid tenantId, Guid userId, bool dismissed)
    {
        var result = await SetOnboardingDismissalEndpoint.Handle(
            new SetOnboardingDismissalEndpoint.Request(dismissed),
            new FixedTenantService(tenantId, connectionString),
            new FixedUserContext(userId, tenantId),
            _factory,
            CentralDb(),
            CancellationToken.None);

        return result.Value!;
    }

    private static OnboardingStep Step(OnboardingStatusResponse status, string key)
        => status.Steps.Single(s => s.Key == key);
}
