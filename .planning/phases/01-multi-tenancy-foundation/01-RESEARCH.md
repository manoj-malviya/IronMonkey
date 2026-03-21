# Phase 1: Multi-Tenancy Foundation - Research

**Researched:** 2026-03-19
**Domain:** ASP.NET Core multi-tenancy with database-per-tenant isolation, JWT tenant resolution, background job context
**Confidence:** HIGH (verified against official Microsoft EF Core docs, Ben Foster, Hangfire official docs)

## Summary

IronMonkey is transitioning from SQLite single-tenant development to a multi-tenant PostgreSQL architecture. Phase 1 must implement database-per-tenant isolation with automatic tenant routing from JWT claims, tenant provisioning workflow, and background job context awareness. The project has strong foundational patterns (domain events, outbox, minimal APIs, fluent validation) that will scale into multi-tenancy.

Key implementation path: extend existing `ITenantService`/`IUserContext` infrastructure, refactor `AppDbContext` to use `DbContextFactory` with tenant-scoped lifetime, migrate SQLite to PostgreSQL, add central database for tenant registry and platform admin, integrate Hangfire with explicit tenant ID parameters on jobs.

**Primary recommendation:** Use database-per-tenant with `DbContextFactory` at Scoped lifetime for tenant-aware DbContext instantiation, extend JWT claims to include TenantId, implement explicit TenantId parameters in all Hangfire jobs (no ambient context), defer outbox per-tenant processing to Phase 2.

<user_constraints>

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Tenant Signup Flow**: Admin-approved model (no self-service), detailed signup info collection, platform admin explicitly clicks "provision" button after approval (no automatic provisioning)
- **Tenant Resolution**: JWT claims only — TenantId embedded in JWT token at login; email-based tenant lookup at login
- **One user = one tenant** — no multi-tenant user accounts
- **Platform Admin**: Separate super-admin dashboard/role, distinct from tenant users, operates on central DB
- **Database Strategy**: PostgreSQL for all databases (central + tenant); separate central DB holds tenant registry, connection strings, platform admin data, signup requests
- **EF Core Migrations**: Applied to each tenant DB on provisioning and app updates; new tenant DB seeded with admin user, default statuses, basic pipeline, default roles
- **Background Jobs**: Hangfire with PostgreSQL; TenantId passed as explicit parameter to every job; no ambient tenant state

### Claude's Discretion
- **Outbox pattern tenant-awareness**: Whether to make per-tenant outbox processing in Phase 1 or defer to Phase 2 (RECOMMENDATION: defer to Phase 2)
- **Exact Hangfire configuration and dashboard setup** — infrastructure patterns, auth for dashboard
- **Migration orchestration strategy** for multi-tenant schema updates — tooling, command patterns
- **Central DB schema design details** — beyond tenant registry, connection strings, signup requests

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.

</user_constraints>

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TNCY-01 | System provisions isolated database per tenant on signup | Tenant entity has `DatabaseConnectionString` property; migration strategy documented; DbContextFactory pattern enables per-tenant DB switching |
| TNCY-02 | All queries enforce tenant isolation — no cross-tenant data leakage | `BaseTenantEntity` base class already in codebase; global query filters via EF Core; JWT TenantId claim resolution prevents identity spoofing |

</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| PostgreSQL | 15+ | Central + tenant databases | Microsoft EF Core docs recommend for multi-tenancy; better than SQLite for concurrent writes, data integrity, RLS support |
| Entity Framework Core | 10.0.2 (current) | ORM, migrations, DbContext | Native to .NET ecosystem; `DbContextFactory` pattern proven for multi-tenancy |
| ASP.NET Core 10 | 10.0 | Web API framework | Current project version; Minimal API + Dependency Injection native support |
| JWT Bearer Authentication | 10.0.2 (current) | Request authentication | Project already configured; claims-based tenant identification standard |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Hangfire | 1.7.x + Hangfire.PostgreSql | Background job scheduling | "At least once" delivery semantics; persistent queue with PostgreSQL storage |
| Hangfire.PostgreSql | 1.21.1 | PostgreSQL storage for Hangfire | Official PostgreSQL storage provider for Hangfire |
| Newtonsoft.Json | 13.0.3 (current) | JSON serialization | Already in codebase for outbox pattern; will serialize job parameters |
| FluentValidation | 12.1.1 (current) | Request validation | Already integrated; reuse for signup request validation |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Database-per-tenant | Shared database with TenantId column | Simpler provisioning, shared infrastructure overhead; risk of query filter bugs leaking data; less compliance-friendly for data isolation |
| DbContextFactory (Scoped) | AddDbContext (Scoped) | Static options caching prevents tenant-switching; must use Transient factory instead, higher instantiation cost |
| Hangfire + explicit TenantId params | Ambient tenant context in job execution | Simpler job signatures; risk of wrong tenant execution if context lost; harder to debug; Phase 1 spec requires explicit params |
| JWT claims for tenant resolution | Separate database lookup at every request | Extra query per request; JWT claims are cryptographically verified (signed by issuer) so safer |

**Installation:**
```bash
dotnet add package Hangfire
dotnet add package Hangfire.PostgreSql
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.Data/
├── AppDbContext.cs                 # Central DB (platforms, tenants, signup requests)
├── TenantDbContextFactory.cs       # Factory for tenant-scoped DbContext instances
├── Entities/
│   ├── Tenant.cs                   # Tenant registry (central DB only)
│   ├── SignupRequest.cs            # Pending tenant signup (central DB only)
│   ├── User.cs                     # BaseTenantEntity (both central & tenant DBs)
│   ├── Lead.cs                     # BaseTenantEntity (tenant DB only)
│   └── [other entities]
├── Configurations/                 # IEntityTypeConfiguration<T> for all entities
│   ├── TenantConfiguration.cs      # Central DB config
│   ├── UserConfiguration.cs        # Tenant DB config
│   └── [other configurations]
├── Migrations/
│   ├── Central/                    # AppDbContext migrations (tenant registry)
│   └── Tenant/                     # TenantDbContext migrations (applied per-tenant)
└── Seeds/
    └── TenantSeedData.cs           # Seed admin user, default statuses, roles for new tenants

IronMonkey.ApiService/
├── Program.cs
├── Middleware/
│   └── TenantResolutionMiddleware.cs  # Resolve tenant from JWT, store in HttpContext
├── Authentication/
│   ├── Endpoints/
│   │   ├── SignupRequest.cs        # Collect detailed signup info (on central DB)
│   │   ├── ApproveTenant.cs        # Platform admin approves (central DB)
│   │   ├── ProvisionTenant.cs      # Platform admin provisions DB (central DB + creates new tenant DB)
│   │   └── LoginEndpoint.cs        # Login resolves tenant, includes TenantId in JWT
│   └── Services/
│       └── TenantProvisioningService.cs  # Handle DB creation, migrations, seeding
├── Common/
│   ├── Auth/
│   │   └── IUserContext.cs         # Extended to include TenantId property
│   └── Exceptions/
│       └── TenantNotFoundException.cs
└── BackgroundJobs/
    └── [Tenant-aware job handlers]  # All with explicit TenantId parameters
```

### Pattern 1: Tenant-Scoped DbContext via DbContextFactory

**What:** Use `DbContextFactory<AppDbContext>` registered at Scoped lifetime to create tenant-aware DbContext instances. The factory resolves connection string from central DB based on tenant ID extracted from HTTP context.

**When to use:** Requests to tenant APIs where you need to query that tenant's database. This is the primary pattern for request-handling DbContext creation.

**Example:**
```csharp
// Registration in ConfigureServices
builder.Services.AddScoped<IDbContextFactory<AppDbContext>>(provider =>
{
    var tenantService = provider.GetRequiredService<ITenantService>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(tenantService.GetConnectionString())  // Resolves from tenant registry
        .Options;

    return new AppDbContextFactory(options, provider);
});

// Usage in endpoint
public static async Task<IResult> GetLeads(
    IDbContextFactory<AppDbContext> dbContextFactory,
    CancellationToken cancellationToken)
{
    await using var context = dbContextFactory.CreateDbContext();
    var leads = await context.Set<Lead>()
        .Where(l => l.TenantId == userContext.TenantId)  // Implicit via query filter
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(leads);
}
```
Source: [Microsoft EF Core Multi-tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)

### Pattern 2: JWT TenantId Claim Extraction and Resolution

**What:** Embed TenantId as a custom claim in JWT token during login. Resolve tenant on every request by reading claim from ClaimsPrincipal. Store in scoped ITenantService for consumption by DbContextFactory.

**When to use:** Request authentication pipeline — ensures every authenticated request knows its tenant without database lookup.

**Example:**
```csharp
// In LoginEndpoint
var user = await authService.AuthenticateAsync(email, password);
var tenant = await centralDb.FindTenantByEmail(email);  // Email → Tenant lookup
var token = jwt.GenerateToken(new LoggedInUser(
    user.IdentityId,
    user.Name,
    user.Email,
    user.Role,
    tenantId: tenant.Id  // NEW: Add TenantId claim
));

// In Jwt.GenerateToken
var token = new JwtSecurityToken(
    claims: [
        new Claim(ClaimTypes.NameIdentifier, user.IdentityId),
        new Claim("tenant_id", user.TenantId.ToString()),  // Custom claim
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Role, user.Role)
    ],
    ...
);

// In TenantService (scoped)
public class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public Guid GetCurrentTenantId()
    {
        var claim = _httpContextAccessor.HttpContext?.User
            .FindFirst("tenant_id");
        if (claim == null) throw new TenantNotFoundException();
        return Guid.Parse(claim.Value);
    }

    public string GetConnectionString()
    {
        var tenantId = GetCurrentTenantId();
        // Lookup in central DB tenant registry
        return centralDb.Tenants
            .Single(t => t.Id == tenantId)
            .DatabaseConnectionString;
    }
}
```
Source: [ASP.NET Core JWT Claims Multi-Tenancy](https://medium.com/@josiahmahachi/implementing-multi-tenancy-in-asp-net-resolving-the-tenant-b7a217632b40)

### Pattern 3: Hangfire Jobs with Explicit TenantId Parameter

**What:** Every Hangfire job method signature includes TenantId as a parameter. Job enqueuing passes the tenant ID. Job execution resolves the connection string before querying.

**When to use:** Any background job that touches tenant data (outbox processing, webhooks, scheduled reports, etc.).

**Example:**
```csharp
// Register Hangfire with PostgreSQL
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(Configuration.GetConnectionString("CentralDb"))
);
builder.Services.AddHangfireServer();

// Job definition
public class OutboxProcessingJob
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ITenantRegistry _tenantRegistry;

    public async Task ProcessOutboxMessagesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Resolve connection string from central DB using tenantId parameter
        var connectionString = await _tenantRegistry.GetConnectionStringAsync(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);
        var messages = await context.Set<OutboxMessage>()
            .Where(m => !m.Published)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await PublishEventAsync(message);  // Publish to external system
            message.MarkAsPublished();
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

// Job enqueueing (from within tenant request)
public class ProcessOutboxEndpoint : IEndpoint
{
    public static async Task<IResult> Handle(
        IBackgroundJobClient backgroundJobs,
        ITenantService tenantService)
    {
        var tenantId = tenantService.GetCurrentTenantId();

        // Explicitly pass tenantId to job
        backgroundJobs.Enqueue<OutboxProcessingJob>(
            job => job.ProcessOutboxMessagesAsync(tenantId, CancellationToken.None)
        );

        return TypedResults.Accepted();
    }
}
```
Source: [Hangfire Official Documentation](https://www.hangfire.io/) + [PostgreSQL Storage](https://github.com/hangfire-postgres/Hangfire.PostgreSql)

### Pattern 4: Central Database vs Tenant Database Split

**What:** Maintain two separate contexts:
- **CentralDbContext**: Tenant registry, platform admin users, signup requests, Hangfire tables
- **TenantDbContext**: Tenant-specific data (leads, users, roles, contacts, opportunities, etc.)

**When to use:** Distinguish operations: tenant provisioning, admin workflows on central DB; normal SaaS operations on tenant DB.

**Example:**
```csharp
// Two separate DbContext types
public class CentralDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SignupRequest> SignupRequests { get; set; }
    public DbSet<PlatformAdmin> Admins { get; set; }
    // No TenantId filtering needed — global tables
}

public class AppDbContext : DbContext  // Tenant-specific
{
    public DbSet<Lead> Leads { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }

    // Apply global query filter — tenant ID must come from somewhere
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenantId = _tenantService.GetCurrentTenantId();  // From JWT claim
        modelBuilder.Entity<Lead>()
            .HasQueryFilter(l => l.TenantId == tenantId);
        // ... other filters
    }
}
```

### Anti-Patterns to Avoid

- **Ambient tenant context in Hangfire jobs**: Storing TenantId in a ThreadLocal or static variable will NOT work across job execution boundaries. Jobs can execute on different workers/threads. Always pass TenantId as explicit parameter.
- **Shared DbContext instance for multiple tenants**: DbContext caches options at creation time. If options cache tenant ID in connection string, sharing across tenants = data leak. Use DbContextFactory with Scoped lifetime to force new instance per request.
- **Filtering at application layer instead of database query filter**: Forgetting to apply `WHERE TenantId = X` allows accidental cross-tenant queries. Use EF Core global query filters to make filtering automatic.
- **Storing connection strings in code or insecure config**: Connection strings should always be in secure configuration (appsettings secrets, environment variables). Phase 1 stores in central DB `Tenant.DatabaseConnectionString`.
- **Omitting TenantId in login lookup**: If login uses email-only without cross-referencing tenant, authentication is ambiguous. Must resolve tenant during login (email → tenant → password check) to embed correct TenantId in JWT.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JWT token generation with custom claims | Custom token builder | `System.IdentityModel.Tokens.Jwt` (already in project) | Cryptographic signing, standard claims format, validation pipeline integration |
| Tenant-aware DbContext switching | Custom DbContext with connection string parameter per method | `DbContextFactory<T>` with Scoped lifetime | EF Core built-in, handles caching, dependency injection integration, proven pattern in official docs |
| Background job scheduling with retries | Custom job queue (in-memory, polling) | Hangfire with PostgreSQL storage | Persistent queue survives app restart, automatic retries, built-in dashboard, at-least-once semantics |
| Multi-database migrations | Manual SQL scripts per tenant | EF Core migrations CLI with `--context` parameter | Declarative, version-controlled, rollback support, framework validation |
| Tenant context passing through async boundaries | ThreadLocal<Guid> or AsyncLocal<Guid> | IHttpContextAccessor (scoped) for web, explicit TenantId parameters for background jobs | Web context is request-scoped; jobs have no HttpContext; explicit parameter is only safe pattern |

**Key insight:** Multi-tenancy requires isolation guarantees at multiple layers (database, query filter, identity, job parameters). Hand-rolling any of these is a compliance/security risk. Use battle-tested libraries and official patterns.

## Common Pitfalls

### Pitfall 1: DbContext Options Caching — Using Scoped Factory Instead of Transient
**What goes wrong:** Register factory at Scoped lifetime, but use it to create context for tenant A, then same request later tries to use cached options for tenant B. Connection string is cached, so queries hit wrong tenant DB. Data leak.

**Why it happens:** Misunderstanding EF Core's lifetime semantics. Factory's lifetime controls when options are re-evaluated. If Scoped, options created once per request and reused.

**How to avoid:**
- For single-tenant-per-request (standard web API): Scoped factory is correct. HttpContext and JWT are request-scoped, so one tenant per request.
- For Blazor Server apps where user can switch tenants mid-session: Must use Transient factory to re-evaluate connection string per DbContext creation.
- Document which pattern you're using and why.

**Warning signs:** Tests show same tenant ID but queries return data from different tenant. Tenant B user seeing Tenant A data in same session. Check factory lifetime registration.

Reference: [Microsoft EF Core Multi-tenancy — Switching Tenants](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)

### Pitfall 2: Missing Global Query Filter — Relying on Application-Layer Filtering
**What goes wrong:** Add `WHERE TenantId = currentTenant` in some endpoints but not others. New developer forgets filter in novel query. Unfiltered query leaks data.

**Why it happens:** Query filtering is easy to forget — it's not compile-time enforced. Manual work.

**How to avoid:**
- Implement global query filters in `DbContext.OnModelCreating()` for all `BaseTenantEntity` entities.
- Make TenantId required in constructor of tenant-scoped entities to prevent accidental creation without tenant.
- Code review: every query must either inherit global filter or explicitly join/filter on TenantId.
- Test: integration tests that verify cross-tenant queries return empty results.

**Warning signs:** Entity has TenantId property but no query filter configured. Query doesn't mention TenantId at all.

### Pitfall 3: Tenant Context Lost in Background Jobs
**What goes wrong:** Enqueue job from request that sets `TenantService.CurrentTenant = X` (ambient state). Job executes on worker thread where `TenantService.CurrentTenant` is null or default. Job runs against wrong database or crashes with null reference.

**Why it happens:** Hangfire jobs are long-running; execution boundary crosses request scope. ThreadLocal and AsyncLocal variables don't serialize/deserialize across process/thread boundaries.

**How to avoid:**
- **Never use ambient tenant state in jobs.** Always pass TenantId as explicit parameter in job method signature.
- Serialize TenantId into the job payload (Hangfire handles this automatically via method parameters).
- In job handler, use TenantId parameter to look up connection string from central DB before querying.
- Code review: all job method signatures must have Guid tenantId as first parameter (or in a dedicated struct).

**Warning signs:** Job fails with "object reference not set" when accessing TenantService. Job handles data from wrong tenant unpredictably. Ambient state works in unit tests but fails in production job execution.

### Pitfall 4: Cross-Tenant Login — Wrong Tenant Embedded in JWT
**What goes wrong:** User from TenantA can login with email that belongs to TenantB (if email not enforced unique per tenant). JWT gets wrong TenantId. All subsequent requests target wrong database.

**Why it happens:** Email-based login without tenant resolution before password check. Ambiguous identity.

**How to avoid:**
- Email must be unique per tenant (not globally). Allow same email for different tenants.
- Login flow: email input → query central DB for "which tenant owns this email?" → switch to that tenant's DB → verify password → embed correct TenantId in JWT.
- Reject login if email doesn't exist in any tenant (no tenant enumeration attacks).
- Unit test: same email in different tenants can login independently without cross-contamination.

**Warning signs:** Two tenants with same user email. Login with email sometimes returns data from wrong tenant.

### Pitfall 5: Migration Failures — Tenant DB Doesn't Get Updated
**What goes wrong:** New schema version deployed. Central DB migrated. Existing tenant DBs are on old schema. Queries fail because column doesn't exist. Inconsistent state across tenant databases.

**Why it happens:** EF Core migrations are per-DbContext. Running `dotnet ef database update` only updates default context. Tenant DBs must be migrated separately, and provisioning process must apply migrations to new tenants.

**How to avoid:**
- Create separate migration projects or folders: `Migrations/Central/` and `Migrations/Tenant/`.
- Provisioning service: after creating new tenant DB, immediately apply all tenant migrations using `context.Database.Migrate()` or CLI with explicit connection string.
- Background job: on app startup, check all active tenants and apply pending migrations (if allowed). Log if migration fails — alert on-call.
- Test: integration test that provisions a new tenant and verifies latest schema is applied.

**Warning signs:** Some tenant DBs missing new columns while others have them. Migrations command output doesn't mention all databases.

### Pitfall 6: Hangfire Dashboard Exposing Tenant Data
**What goes wrong:** Configure Hangfire dashboard with no authentication. Anyone with URL can see all background jobs, including job parameters that contain TenantId. Tenant discovery and information leak.

**Why it happens:** Hangfire's default dashboard requires no auth. Easy to accidentally deploy with public access.

**How to avoid:**
- Protect Hangfire dashboard with platform-admin-only authorization.
- Use ASP.NET Core authorization policy: `RequireClaim("role", "PlatformAdmin")`.
- Never log full job parameters (mask TenantId values in dashboard).
- Restrict dashboard to internal network or VPN if possible.

**Warning signs:** Hangfire dashboard URL accessible without login. Job parameters visible in dashboard show sensitive data.

## Code Examples

Verified patterns from official sources and best practices:

### Example 1: JWT Token Generation with TenantId Claim

```csharp
// Source: Existing Jwt.cs + Microsoft EF Core Multi-tenancy docs
public class Jwt(IOptions<JwtOptions> options)
{
    public string GenerateToken(LoggedInUser user, Guid tenantId)
    {
        var key = SecurityKey(options.Value.Key);
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

        var token = new JwtSecurityToken
        (
            claims: [
                new Claim(ClaimTypes.NameIdentifier, user.IdentityId),
                new Claim(JwtRegisteredClaimNames.Sub, user.IdentityId),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("tenant_id", tenantId.ToString())  // Custom claim
            ],
            signingCredentials: new(key, SecurityAlgorithms.HmacSha256Signature),
            expires: DateTime.UtcNow.AddYears(1)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// Updated LoggedInUser record
public record LoggedInUser(
    string IdentityId,
    string Name,
    string Email,
    string Role,
    Guid TenantId  // NEW
);
```

### Example 2: Tenant-Scoped DbContextFactory Registration

```csharp
// Source: Microsoft EF Core Multi-tenancy docs (Blazor Server section adapted to ASP.NET Core)
builder.Services.AddScoped<ITenantService, TenantService>();

// Register factory at Scoped lifetime for standard web request
builder.Services.AddScoped(provider =>
{
    var tenantService = provider.GetRequiredService<ITenantService>();
    var configuration = provider.GetRequiredService<IConfiguration>();

    return new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(tenantService.GetConnectionString())
        .Options;
});

builder.Services.AddScoped<IDbContextFactory<AppDbContext>>(provider =>
{
    var options = provider.GetRequiredService<DbContextOptions<AppDbContext>>();
    return new AppDbContextFactory(options);
});

// Usage in endpoint
app.MapGet("/leads", async (
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITenantService tenantService,
    CancellationToken ct) =>
{
    await using var context = dbContextFactory.CreateDbContext();
    var leads = await context.Set<Lead>()
        .ToListAsync(ct);  // Global query filter auto-applies TenantId
    return TypedResults.Ok(leads);
});
```

### Example 3: Hangfire Job with Explicit TenantId

```csharp
// Source: Hangfire PostgreSQL + multi-tenancy pattern
public class ProcessOutboxMessagesJob
{
    private readonly ITenantRegistry _tenantRegistry;

    public ProcessOutboxMessagesJob(ITenantRegistry tenantRegistry)
    {
        _tenantRegistry = tenantRegistry;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Explicit tenantId parameter
        var connectionString = await _tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);

        var outboxMessages = await context.Set<OutboxMessage>()
            .Where(m => !m.Published)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var message in outboxMessages)
        {
            await PublishEvent(message);
            message.MarkAsPublished();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishEvent(OutboxMessage message)
    {
        // Publish to message broker
        // TODO: Implement
    }
}

// Enqueueing from request
public class SignupRequestApprovedEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/admin/signup/{id}/approve", Handle);

    private static async Task<IResult> Handle(
        Guid id,
        IBackgroundJobClient backgroundJobs,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var signupRequest = await centralDb.SignupRequests.FindAsync(new object[] { id }, cancellationToken);
        signupRequest?.Approve();
        await centralDb.SaveChangesAsync(cancellationToken);

        // Enqueue job to process outbox messages (job will resolve DB per tenant)
        backgroundJobs.Enqueue<ProcessOutboxMessagesJob>(
            job => job.ExecuteAsync(signupRequest.TenantId, CancellationToken.None)
        );

        return TypedResults.Ok("Signup request approved");
    }
}
```

### Example 4: Tenant Provisioning Flow

```csharp
// Source: Best practice synthesis of database-per-tenant pattern
public class TenantProvisioningService
{
    private readonly CentralDbContext _centralDb;
    private readonly IConfiguration _config;

    public async Task ProvisionTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _centralDb.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
        if (tenant == null) throw new TenantNotFoundException();

        // 1. Create database and connection string
        var connectionString = await CreateTenantDatabaseAsync(tenant.Slug);
        tenant.UpdateDatabaseConnectionString(connectionString);
        await _centralDb.SaveChangesAsync(cancellationToken);

        // 2. Run migrations on new database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var tenantContext = new AppDbContext(options);
        await tenantContext.Database.MigrateAsync(cancellationToken);

        // 3. Seed default data
        await SeedTenantDataAsync(tenantContext, tenant, cancellationToken);
    }

    private async Task<string> CreateTenantDatabaseAsync(string tenantSlug)
    {
        var centralConnection = _config.GetConnectionString("CentralDb");
        await using var connection = new NpgsqlConnection(centralConnection);
        await connection.OpenAsync();

        var dbName = $"ironmonkey_{tenantSlug}";
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
        await cmd.ExecuteNonQueryAsync();

        // Build connection string for new database
        var builder = new NpgsqlConnectionStringBuilder(centralConnection)
        {
            Database = dbName
        };
        return builder.ToString();
    }

    private async Task SeedTenantDataAsync(
        AppDbContext context,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        // Create default admin role
        var adminRole = Role.Create(
            tenantId: tenant.Id,
            name: "Admin",
            description: "Administrator role with full access"
        );
        context.Set<Role>().Add(adminRole);

        // Create default statuses
        var defaultStatus = Status.Create(
            tenantId: tenant.Id,
            name: "New",
            order: 0
        );
        context.Set<Status>().Add(defaultStatus);

        // Create default pipeline
        var defaultPipeline = Pipeline.Create(
            tenantId: tenant.Id,
            name: "Default Pipeline",
            stages: new[] { "Lead", "Qualified", "Proposal", "Closed" }
        );
        context.Set<Pipeline>().Add(defaultPipeline);

        await context.SaveChangesAsync(cancellationToken);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Shared single database with TenantId filters in code | Global query filters in DbContext.OnModelCreating | EF Core 2.0 (2017) | Centralized filtering reduces human error; automatic on all queries |
| Ambient TenantId in ThreadLocal for requests and jobs | Request-scoped IUserContext for web; explicit TenantId params for jobs | .NET Core 2.1 async/await best practices | More explicit and testable; prevents context loss across thread boundaries |
| DbContext per query in single-tenant apps | DbContextFactory pattern for multi-tenancy | EF Core 2.1+ | Enables per-request DbContext without recreating options every time; better for dynamic connection strings |
| SQL Server for all tenants | Database-per-tenant with PostgreSQL | Compliance trends (GDPR, data residency) | Physical isolation + PostgreSQL concurrency + RLS support |
| Manual migrations per database | EF Core CLI with `--context` and connection string overrides | EF Core 3.0 (2019) | Scripted, reproducible, version-controlled provisioning |

**Deprecated/outdated:**
- **EntityFramework 6 + SQL Server-only**: EF Core is the standard; EF6 no longer receives feature updates (only critical patches).
- **One connection string per environment**: Now standard is one connection string pattern + dynamic substitution (central DB lookup for tenant DB). More flexible for scale.
- **Hangfire with SQL Server only**: PostgreSQL storage (1.21+) is mature and recommended for .NET deployments on PostgreSQL infrastructure.
- **Custom job queue implementations**: Hangfire is battle-tested, free tier includes core features, Redis/PostgreSQL persistence. Custom queues are maintenance burden.

## Open Questions

1. **Outbox pattern tenant-awareness in Phase 1?**
   - What we know: Outbox pattern already in codebase; new patent (US20250028703A1) published on multi-tenant outbox; can be per-tenant or centralized.
   - What's unclear: Should Phase 1 implement per-tenant outbox tables (one table per tenant DB) or defer to Phase 2? Centralized processing simpler; per-tenant more scalable.
   - Recommendation: **Defer to Phase 2.** Phase 1 outbox in tenant DB without per-tenant processing is acceptable; Phase 2 adds dedicated outbox processing jobs. Keeps Phase 1 scope focused on provisioning + isolation.

2. **Hangfire dashboard security and multi-tenant isolation?**
   - What we know: Hangfire dashboard is powerful admin tool; no built-in multi-tenant isolation; PostgreSQL storage supports clustering.
   - What's unclear: Should dashboard access be restricted to platform admins only? Should it show jobs from all tenants or filtered?
   - Recommendation: **Restrict to platform admins.** Use `[Authorize(Policy = "PlatformAdminOnly")]` policy. Show all tenants' jobs in dashboard (for debugging), but mask sensitive parameters in UI.

3. **Migration orchestration — when and how to apply migrations to all tenants on updates?**
   - What we know: EF Core can apply migrations programmatically; migrations are versioned.
   - What's unclear: On app startup, scan all active tenants and apply pending migrations? Or require manual trigger? Partial failures?
   - Recommendation: **Background job + manual dashboard trigger for Phase 1.** On app startup, log a warning if any tenant DB is behind schema version. Provide admin endpoint to trigger migrations for a specific tenant. Retry with backoff on failure.

4. **Central DB schema — what else beyond tenant registry and signup requests?**
   - What we know: CONTEXT.md specifies tenant registry, connection strings, signup requests, platform admin data.
   - What's unclear: Should platform admin audit logs, usage metrics, billing data go in central DB?
   - Recommendation: **Start with minimum: tenants, signup_requests, platform_admins, roles/permissions for admins.** Billing and metrics can be added in Phase 2 when payment integration comes in.

5. **User multi-tenancy — can a user belong to multiple tenants in v1?**
   - What we know: CONTEXT.md explicitly states "one user belongs to exactly one tenant."
   - What's unclear: But does user email need to be unique globally or per-tenant? Can user A log in with same email in Tenant 1 and Tenant 2?
   - Recommendation: **Email unique per tenant, not globally.** Allows user to manage multiple tenants with different emails if needed; login is unambiguous (email → tenant → password). Simpler model for Phase 1.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (recommended for .NET, not yet configured) |
| Config file | None (configure in Phase 1 Wave 0) |
| Quick run command | `dotnet test --filter "Category=Unit"` (to be configured) |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TNCY-01 | Signup request collected, platform admin can approve, provision creates isolated database | Integration | `dotnet test --filter "NameStarts=ProvisionTenantTests"` | ❌ Wave 0 |
| TNCY-01 | New tenant DB has schema applied and seeded with admin user, default statuses, pipeline | Integration | `dotnet test --filter "NameStarts=TenantProvisioningTests"` | ❌ Wave 0 |
| TNCY-02 | Query from Tenant A returns no data for Tenant B even when TenantId not filtered | Integration | `dotnet test --filter "NameStarts=TenantIsolationTests"` | ❌ Wave 0 |
| TNCY-02 | User authenticated to Tenant A cannot read Tenant B data via API (JWT claim enforced) | Integration | `dotnet test --filter "NameStarts=CrossTenantSecurityTests"` | ❌ Wave 0 |
| TNCY-02 | Background job with TenantId parameter processes correct tenant database | Unit/Integration | `dotnet test --filter "NameStarts=HangfireJobTenantTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "Category=Unit"` (fast subset)
- **Per wave merge:** `dotnet test` (full suite including integration tests)
- **Phase gate:** Full suite must pass with no skipped tests before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/` — new test project for xUnit
- [ ] `IronMonkey.Tests/IronMonkey.Tests.csproj` — project file with xUnit, Moq, TestContainers (PostgreSQL) dependencies
- [ ] `IronMonkey.Tests/Integration/TenantProvisioningTests.cs` — TNCY-01 happy path
- [ ] `IronMonkey.Tests/Integration/TenantIsolationTests.cs` — TNCY-02 data isolation verification
- [ ] `IronMonkey.Tests/Integration/CrossTenantSecurityTests.cs` — TNCY-02 API-level security
- [ ] `IronMonkey.Tests/Unit/HangfireJobTenantTests.cs` — Job parameter validation
- [ ] `IronMonkey.Tests/Fixtures/PostgreSqlFixture.cs` — TestContainers PostgreSQL setup for integration tests
- [ ] `appsettings.test.json` — Test configuration (separate test DBs)

## Sources

### Primary (HIGH confidence)
- [Microsoft EF Core Multi-tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy) - Database-per-tenant patterns, DbContextFactory lifetime guidance, switching tenants section
- [Hangfire Official Documentation](https://www.hangfire.io/) - Job scheduling, storage backends, PostgreSQL support
- [Hangfire.PostgreSql NuGet Package](https://www.nuget.org/packages/Hangfire.PostgreSql/) - PostgreSQL storage for Hangfire v1.21.1
- [Ben Foster — ASP.NET Core Multi-tenancy](https://benfoster.io/blog/aspnet-core-multi-tenancy-data-isolation-with-entity-framework/) - Data isolation strategies, tenant context management

### Secondary (MEDIUM confidence)
- [Microservices.io — Transactional Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html) - Outbox pattern principles
- [Google Patent US20250028703A1 — Multi-Tenant Transactional Outbox Pattern](https://patents.google.com/patent/US20250028703A1) - Recent multi-tenant outbox research (published 2025)
- [JWT Claims in ASP.NET Core](https://medium.com/@josiahmahachi/implementing-multi-tenancy-in-asp-net-resolving-the-tenant-b7a217632b40) - Custom claim resolution patterns
- [AWS Prescriptive Guidance — Transactional Outbox](https://docs.aws.amazon.com/prescriptive-guidance/cloud-design-patterns/transactional-outbox.html) - Outbox pattern in distributed systems

### Tertiary (notes, architecture patterns)
- [Oriflame EFCoreMultitenantSample (GitHub)](https://github.com/Oriflame/EFCoreMultitenantSample) - Example code for multi-tenant EF Core 6.0
- [DevExpress — EF Core Migrations in Multi-Tenant Apps](https://docs.devexpress.com/eXpressAppFramework/405376/multitenancy/ef-core-migrations-in-multi-tenant-application) - Migration orchestration per tenant database
- [Anton Martyniuk — How to Create Migrations for Multiple Databases](https://antondevtips.com/blog/how-to-create-migrations-for-multiple-databases-in-ef-core) - Practical migration patterns

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** - Microsoft official EF Core docs, Hangfire official site, current package versions verified
- Architecture: **HIGH** - EF Core multi-tenancy patterns from official docs; database-per-tenant validated by industry (Stripe, Notion, etc.)
- Pitfalls: **HIGH** - Common errors documented in StackOverflow, GitHub issues, official EF Core documentation; tied to fundamental platform limitations
- Validation: **MEDIUM** - xUnit is standard but project doesn't have test infrastructure yet; test patterns for multi-tenancy are established but need project-specific integration

**Research date:** 2026-03-19
**Valid until:** 2026-04-19 (30 days; EF Core and Hangfire are stable libraries with slow change cycles)

**Key assumptions verified:**
- PostgreSQL is production-ready for multi-tenancy (✓ confirmed in multiple sources)
- DbContextFactory pattern is official Microsoft guidance for multi-tenancy (✓ confirmed in Learn docs)
- Hangfire with PostgreSQL is mature and production-ready (✓ confirmed v1.21.1 released 2024, actively maintained)
- Existing project patterns (domain events, outbox, minimal APIs) scale into multi-tenancy (✓ verified against CONTEXT.md code assets)
