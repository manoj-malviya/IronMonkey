# Stack Research: Multi-Tenant Lead Management SaaS on .NET Aspire

**Domain:** Configurable multi-tenant Lead Management SaaS with database-per-tenant isolation, dynamic schema, and omnichannel communications

**Researched:** 2026-03-19

**Confidence:** HIGH (EF Core, .NET Aspire, Blazor stable; newer libraries like Stateless 5.20.1 verified with current versions)

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET | 10.0 | Runtime and framework | Already standardized in project; latest stable with full Aspire support |
| ASP.NET Core | 10.0.2 | Web framework for API | Minimal APIs + Blazor Server pattern matches existing architecture; built-in auth & DI |
| Blazor Server | 10.0.2 | Interactive frontend | Real-time updates via SignalR, server-side state, integrated auth (no JWT duplication) |
| Entity Framework Core | 10.0.2 | ORM and data access | Native support for JSON columns (dynamic fields), global query filters (tenant isolation), DB-per-tenant strategies |
| PostgreSQL | 15+ | Primary database | JSONB columns for dynamic fields, array types, superior JSON querying vs SQLite; multi-tenant friendly |
| .NET Aspire | 13.1.0 | Service orchestration | Already in use; integrates AppHost, service discovery, health checks, observability |
| OpenTelemetry | 1.14.0 | Distributed tracing | Production observability for multi-service architecture |

### Multi-Tenancy & Data Isolation

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 | PostgreSQL provider for EF Core | Required for PostgreSQL JSONB + JSON complex types (EF Core 10 feature) | HIGH |
| Custom Tenant Middleware | — | Tenant context resolution | Read tenant from HTTP header (X-Tenant-Id) or subdomain, store in scoped service | HIGH |
| EF Core Global Query Filters | Built-in | Automatic tenant data filtering | Filter all queries by tenant ID at DbContext level; prevents accidental cross-tenant leaks | HIGH |
| Finbuckle.MultiTenant | 6.x+ | Optional: pre-built multi-tenant framework | Only if building custom middleware becomes complex; check compatibility with DB-per-tenant | MEDIUM |

### Workflow & State Management

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Stateless | 5.20.1 | State machine for lead lifecycle | Define lead statuses, transitions, and state-based auto-actions (assign, notify, escalate) | HIGH |
| Workflow Core | 3.x+ | Long-running workflow orchestration | Optional: for complex multi-step workflows with persistence (defer to Phase 2) | MEDIUM |
| MediatR | 12.x+ | Command/event pattern orchestration | Dispatch domain events (lead created, status changed) for decoupled business logic | MEDIUM |

### Dynamic Schema & Configurable Fields

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| System.Text.Json | Built-in (.NET 10) | JSON serialization | Serialize/deserialize dynamic field configs; native AOT compatible |  HIGH |
| Npgsql JSONB | Built-in | JSON storage in PostgreSQL | Store custom field definitions and values in JSONB columns; queryable via EF Core 10 |  HIGH |
| NJsonSchema | 11.x+ | JSON Schema generation & validation | Generate JSON schema from C# types; validate tenant field configs against schema | MEDIUM |
| BlazorJsonForm | 0.x (GitHub) | Dynamic form rendering from JSON schema | Render lead/customer custom fields in Blazor UI without code generation | MEDIUM |

### Communication Providers

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Twilio | 7.14.3 | Email, SMS, WhatsApp API | Send transactional emails, SMS, WhatsApp messages to leads via unified API | HIGH |
| Twilio.AspNet.Core | 8.1.2 | ASP.NET Core integration for Twilio | Register Twilio client in DI, validate Twilio webhooks in middleware | HIGH |
| MailKit | 4.x+ | SMTP email library (alternative) | If tenant has on-premise SMTP server; more control than managed email providers | MEDIUM |
| SendGrid | 9.x+ | Email-only alternative | Lower cost than Twilio for email-only use cases; good secondary provider | LOW |

### UI & Dashboard Components

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| MudBlazor | 9.x | Material Design Blazor components | DataGrid, DataTable, Charts, Dialog, Form components for dashboards and CRUD; Material Design by default | HIGH |
| Microsoft.FluentUI.AspNetCore.Components | 10.x | Fluent Design Blazor components | Alternative: Microsoft design language, strong accessibility, design tokens | MEDIUM |
| Radzen.Blazor | 4.x+ | Pre-built dashboard components | Free, open-source; includes sample dashboard template | LOW |
| ApexCharts.Blazor | 1.x+ | Interactive charts | Pipeline overview, conversion rate, agent performance dashboards | MEDIUM |

### Validation & Business Rules

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| FluentValidation | 12.1.1 | Fluent validation framework | API request validation, domain model validation; already in use | HIGH |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | DI integration | Auto-register validators | HIGH |

### Background Jobs & Scheduling

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Hangfire | Latest (2025) | Background job processing + recurring tasks | Send scheduled follow-up notifications, batch email/SMS, recurring reports; persistent queue | HIGH |
| Hangfire.SqlServer or Hangfire.PostgreSQL | Latest | Hangfire persistence storage | Use PostgreSQL storage for consistency with primary database | HIGH |
| Quartz.NET | 3.x+ | Advanced job scheduling | If complex cron expressions or calendar-based scheduling needed (defer to Phase 2) | LOW |

### API Documentation & Versioning

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Swashbuckle.AspNetCore | 10.1.0 | Swagger/OpenAPI generation | Existing; maintain for API documentation and testing | HIGH |
| Asp.Versioning | 8.1.1 | API versioning | Existing; support multiple API versions for backward compatibility | HIGH |

### Health Checks & Observability

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| AspNetCore.HealthChecks | 6.x+ | Health check endpoints | Expose /health and /alive endpoints for load balancers, Aspire orchestration | MEDIUM |
| AspNetCore.HealthChecks.PostgreSQL | 6.x+ | Database health checks | Monitor PostgreSQL connectivity and readiness | MEDIUM |
| Aspire Dashboard | Built-in (via .NET Aspire) | Observability UI | Monitor services, logs, traces in development; already in use | HIGH |

### Authentication & Authorization

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| ASP.NET Core Identity | 10.0.2 | User management | Already in use; integrate with tenant context for per-tenant user isolation | HIGH |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.2 | JWT token validation | For API clients; already in use | HIGH |
| Custom RBAC Claims | — | Role-based access control | Extend Identity with tenant-specific roles and dynamic permissions (defer to Phase 1B) | HIGH |

## Installation & Versioning

### NuGet Packages

```bash
# Core (existing)
dotnet add package Microsoft.AspNetCore.App --version 10.0.2
dotnet add package EntityFrameworkCore --version 10.0.2

# Database: PostgreSQL (migrate from SQLite)
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.1

# Multi-tenancy: State Machine for lead lifecycle
dotnet add package Stateless --version 5.20.1

# Communications
dotnet add package Twilio --version 7.14.3
dotnet add package Twilio.AspNet.Core --version 8.1.2

# Blazor UI Components
dotnet add package MudBlazor --version 9.x

# Background Jobs
dotnet add package Hangfire.Core --version latest
dotnet add package Hangfire.PostgreSQL --version latest

# Optional: JSON Schema validation
dotnet add package NJsonSchema --version 11.x

# Optional: Dynamic form builder
# (BlazorJsonForm is GitHub-based; add via git submodule or package)

# Health Checks
dotnet add package AspNetCore.HealthChecks.PostgreSQL --version 6.x
```

### .csproj Configuration

```xml
<!-- Ensure .NET 10 target -->
<TargetFramework>net10.0</TargetFramework>

<!-- Enable nullable reference types & implicit usings (existing) -->
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

## Recommended Database Migration

### From SQLite to PostgreSQL

**Why:**
- Native JSONB for dynamic field storage (queryable)
- Superior multi-tenancy support via schema-per-tenant alternative
- EF Core 10 JSON complex types work best with PostgreSQL
- Production-grade for multi-tenant SaaS

**Migration Steps:**
1. Keep SQLite for dev/test; use PostgreSQL for staging/production
2. Configure Npgsql provider in DbContext via `optionsBuilder.UseNpgsql()`
3. Create separate connection strings for each tenant database
4. Use EF Core migrations for schema generation
5. Populate JSONB columns with dynamic field definitions

**Connection String Pattern (DB-per-Tenant):**
```
Server=localhost;Database=ironmonkey_tenant_{tenantId};Port=5432;Username=postgres;Password=...
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| PostgreSQL | Azure SQL + SQL Server | If using Azure infrastructure; supports JSON but less JSONB flexibility |
| PostgreSQL | SQLite | Dev/test only; poor multi-tenant support; limited JSON capabilities |
| Stateless | Workflow Core | If state transitions are complex (sub-states, async waits); adds complexity |
| Twilio | 360NRS or MessageBird | Lower cost for SMS-only; less mature SDKs; Twilio is market standard |
| MudBlazor | DevExpress Blazor | More enterprise features, higher cost, more opinionated |
| MudBlazor | Fluent UI Blazor | Microsoft ecosystem alignment, better accessibility; less feature-rich |
| Hangfire | Azure Service Bus | Cloud-only; higher cost; less visibility into queued jobs |
| Custom Tenant Middleware | Finbuckle.MultiTenant | Pre-built if middleware becomes complex; adds dependency; verify DB-per-tenant compatibility |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| SQLite for production | Poor multi-tenant support, no JSONB, limited concurrency | PostgreSQL 15+ |
| Entity Framework 9.x or earlier | Lacks JSON complex types, limited global filter options | EF Core 10.0.2+ |
| Windows Workflow Foundation (WWF) | Overly complex, deprecated for new projects | Stateless for simple workflows, Workflow Core for complex |
| Custom role-based auth | High maintenance, security risks, not domain-agnostic | ASP.NET Identity + Claims-based authorization |
| Newtonsoft.Json exclusively | Legacy; System.Text.Json is faster, AOT-compatible, built-in | System.Text.Json (with Newtonsoft fallback only if needed) |
| Azure Table Storage for tenant configs | Not suitable for multi-tenant relational data | PostgreSQL JSONB columns |
| Blazor WebAssembly for SaaS UI | Stateless, requires full API duplication, slower initial load | Blazor Server (existing choice) |
| Email service without SMTP fallback | Risk of vendor lock-in, single point of failure | Twilio + MailKit (SMTP) fallback |

## Stack Patterns by Variant

### Database-per-Tenant with Connection String Routing
- Use: Tenant middleware resolves connection string from tenant registry
- Because: Full data isolation, tenant-specific backups/migrations, compliance-friendly
- EF Core: No schema filters; instead, DbContext uses correct connection string per request

### Schema-per-Tenant with Global Query Filters
- Use: All tenants in one database; EF Core global filters by tenant_id
- Because: Simpler operations team experience, shared backups, lower infrastructure cost
- Trade-off: Less isolation; global filters must never be bypassed

### Hybrid: Multi-Tenant + Shared Config Database
- Recommended for IronMonkey
- Shared database: Tenant metadata, user accounts, global roles
- Tenant databases: Lead, customer, workflow data (DB-per-tenant)
- Rationale: Decouples tenant onboarding from data isolation; allows industry recipes in shared config

## Version Compatibility Matrix

| Package | Requires | Reason |
|---------|----------|--------|
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1 | EntityFrameworkCore >= 10.0.4 && < 11.0 | EF Core 10 JSON complex types |
| Stateless 5.20.1 | .NET 4.6.2+ (includes .NET 10) | Compatible with all .NET versions; no special requirements |
| Twilio 7.14.3 | .NET 6.0+ (.NET Standard 2.1) | Targets latest .NET versions; includes .NET 10 |
| MudBlazor 9.x | Blazor Server .NET 8, 9, 10 | Current stable; next major may require .NET 11+ |
| Hangfire (2025) | .NET 6.0+ | Recent updates focused on current .NET versions |
| Microsoft.AspNetCore packages | .NET 10.0.2 | Implicit dependency; must match runtime version |

### Critical Compatibility Notes
- **EF Core 10 JSON Complex Types:** Only available in Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1+; older versions require owned entity workaround
- **Blazor Server with .NET 10:** All Blazor component libraries must explicitly support .NET 10 (check NuGet package details)
- **Hangfire + PostgreSQL:** Use Hangfire.PostgreSQL package (separate from Hangfire.Core) for database persistence
- **Stateless + Async Workflows:** Stateless 5.20+ supports async state machines; earlier versions do not

## Key Architecture Decisions

### Multi-Tenancy Strategy
- **Chosen:** Database-per-tenant + Shared tenant registry database
- **Rationale:**
  - DB-per-tenant isolates customer data completely (regulatory, compliance)
  - Shared registry (PostgreSQL) maintains industry recipes, global user metadata
  - EF Core uses connection string routing; no schema filters needed
  - Allows tenant-independent scaling, backups, and migrations

### Dynamic Schema Implementation
- **Chosen:** JSONB columns in PostgreSQL + EF Core JSON complex types
- **Rationale:**
  - Native JSONB is queryable (LINQ translates to PostgreSQL operators)
  - Avoids EAV pattern (slow, complex queries)
  - Custom field values stored per lead/customer in JSONB
  - Schema definitions in shared registry or per-tenant config table
  - Blazor forms render from JSON schema; no code generation

### State Machine for Workflows
- **Chosen:** Stateless 5.20.1 for lead lifecycle transitions
- **Rationale:**
  - Simple, lightweight, no persistence needed (store state in database)
  - Fluent API; easy to extend with auto-actions (via MediatR events)
  - Defer Workflow Core (Phase 2) until multi-step, long-running workflows needed

### Communication Channels
- **Chosen:** Twilio for SMS/WhatsApp/Email
- **Rationale:**
  - Unified API; one integration for multiple channels
  - Production-grade reliability; market standard for SaaS
  - SMTP fallback via MailKit for on-premise email
  - Built-in webhook support for delivery/bounce tracking

### UI Component Library
- **Chosen:** MudBlazor 9.x
- **Rationale:**
  - Material Design by default; professional appearance
  - Rich DataGrid, Charts, Dialog components
  - Active community; extensive documentation
  - Pairs well with Blazor Server (real-time updates via SignalR)
  - Free/open-source; no licensing costs

## Deployment & Configuration

### Environment-Specific Overrides
```json
{
  "Aspire": {
    "Dashboard": {
      "Enabled": true
    }
  },
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Server=...",
    "TenantRegistry": "Server=..."
  },
  "Twilio": {
    "AccountSid": "${TWILIO_ACCOUNT_SID}",
    "AuthToken": "${TWILIO_AUTH_TOKEN}"
  },
  "Hangfire": {
    "PostgresqlConnection": "Server=...",
    "DashboardEnabled": true
  }
}
```

### Container Images
- Base: `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime)
- Build: `mcr.microsoft.com/dotnet/sdk:10.0` (build stage)

## Sources

### Official Documentation (HIGH confidence)
- [Microsoft Learn: EF Core Multi-Tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [Microsoft Learn: Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Microsoft Learn: Role-Based Authorization in ASP.NET Core 10.0](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0)
- [.NET Aspire Documentation: Health Checks](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks)

### Library Documentation & Releases (HIGH confidence)
- [Stateless NuGet 5.20.1](https://www.nuget.org/packages/stateless/)
- [Twilio .NET SDK Version 7.14.3](https://www.nuget.org/packages/Twilio)
- [Twilio.AspNet.Core Version 8.1.2](https://www.nuget.org/packages/Twilio.AspNet.Core)
- [MudBlazor GitHub Repository](https://github.com/MudBlazor/MudBlazor) — Version 9.x active releases

### Community Best Practices (MEDIUM confidence)
- [Multi-Tenant Applications With EF Core — Milan Jovanovic](https://www.milanjovanovic.tech/blog/multi-tenant-applications-with-ef-core)
- [EF Core and PostgreSQL JSON: Deep Integration in EF Core 9+](https://medium.com/@gunesramazan/deep-json-integration-in-ef-core-9)
- [Blazor Dynamic Forms with JSON Schema — Syncfusion Blogs](https://www.syncfusion.com/blogs/post/create-dynamic-form-builder-in-blazor)
- [Background Jobs in .NET 2025: Hangfire vs Quartz — Code Chronicles](https://medium.com/net-code-chronicles/background-jobs-schedulers-dotnet-abfbf49aa79f)

### Related Research
- Finbuckle.MultiTenant Framework (optional pre-built multi-tenancy)
- Wolverine 4.0 (future EF Core multi-tenancy improvements; still in preview)
- BlazorJsonForm (GitHub-based dynamic form builder from JSON schema)

---

**Research Date:** 2026-03-19

**Next Steps:**
1. Migrate SQLite → PostgreSQL for production database
2. Implement tenant middleware + EF Core connection string routing (Phase 1)
3. Build dynamic field system using JSONB + Blazor forms (Phase 1B)
4. Integrate Twilio for SMS/WhatsApp (Phase 2)
5. Implement Stateless state machine for lead lifecycle (Phase 2)
6. Add Hangfire background jobs for scheduled notifications (Phase 2)
