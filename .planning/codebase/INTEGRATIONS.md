# External Integrations

**Analysis Date:** 2026-03-19

## APIs & External Services

**Email Delivery:**
- SMTP - Generic SMTP server for email delivery
  - Client: System.Net.Mail.SmtpClient (built-in .NET)
  - Configuration: `Smtp` section in appsettings.json
  - Default server: smtp.freesmtpservers.com (development)

## Data Storage

**Databases:**
- SQLite
  - Connection: `DefaultConnection` connection string (appsettings.json)
  - Client: Entity Framework Core with SQLite provider (`Microsoft.EntityFrameworkCore.Sqlite`)
  - Location: Local file path specified in connection string (e.g., `D:\riterr.sqlite`)

**In-Memory Database (Testing):**
- EF Core InMemory provider for test scenarios
  - Package: `Microsoft.EntityFrameworkCore.InMemory`

**File Storage:**
- Local filesystem only - No external file storage service configured

**Caching:**
- Distributed Memory Cache - In-process memory caching
  - Registered in `IronMonkey.ApiService/ConfigureServices.cs` via `AddDistributedMemoryCache()`
  - Interface: `IDistributedMemoryCache` (standard ASP.NET Core)

## Authentication & Identity

**Auth Provider:**
- Custom JWT-based authentication
  - Implementation: `IronMonkey.Common/Auth/Jwt.cs`
  - Approach: Token generation and validation using `System.IdentityModel.Tokens.Jwt`
  - Key storage: Loaded from `Jwt:Key` in appsettings.json

**User Identity Management:**
- ASP.NET Core Identity EntityFrameworkCore integration
  - Entity: `User` in `IronMonkey.Data/Entities/User.cs` (extends `BaseTenantEntity`)
  - Storage: SQLite via EF Core
  - Password: Stored as plain string (development pattern - not production-ready)

**Authorization:**
- Role-based access control (RBAC)
  - Roles entity: `IronMonkey.Data/Entities/Role.cs`
  - Permissions entity: `IronMonkey.Data/Entities/Permission.cs`
  - Custom policy provider: `PermissionAuthorizationPolicyProvider` in `IronMonkey.ApiService/Common/Auth/`

## Monitoring & Observability

**Error Tracking:**
- None configured - No external error tracking service (e.g., Sentry, Datadog)

**Logs:**
- Serilog with ASP.NET Core integration
  - Configuration: Serilog settings in appsettings.json
  - Console/file output (standard Serilog sinks)

**Distributed Tracing:**
- OpenTelemetry
  - OTLP (OpenTelemetry Protocol) exporter for traces and metrics
  - Configuration: Checks for `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable
  - Instrumentation: ASP.NET Core requests, HTTP client calls, runtime metrics
  - Implementation: `IronMonkey.ServiceDefaults/Extensions.cs`

**Health Checks:**
- ASP.NET Core health checks
  - Endpoints: `/health` (readiness), `/alive` (liveness)
  - Custom checks: Extensible via `AddHealthChecks()` in ServiceDefaults

## CI/CD & Deployment

**Hosting:**
- .NET Aspire orchestration (development/local)
- Container-ready (multi-stage Dockerfile patterns supported)
- Health endpoints available for Kubernetes probes

**CI Pipeline:**
- Not configured - No GitHub Actions, Azure DevOps, or other CI service detected

**Deployment Infrastructure:**
- Service Discovery: Built-in via `Microsoft.Extensions.ServiceDiscovery`
- Resilience: Polly-based resilience handlers via `Microsoft.Extensions.Http.Resilience`

## Environment Configuration

**Required env vars:**
- `Jwt:Key` - JWT signing key (from appsettings.json or environment override)
- `OTEL_EXPORTER_OTLP_ENDPOINT` - OpenTelemetry OTLP endpoint (optional, enables telemetry export)

**Connection Strings:**
- `DefaultConnection` - SQLite database path (required)
  - Example: `Data Source=D:\riterr.sqlite`

**SMTP Configuration (Secrets):**
- `Smtp:Server` - SMTP server hostname
- `Smtp:Port` - SMTP port number
- `Smtp:Username` - SMTP authentication username
- `Smtp:Password` - SMTP authentication password
- `Smtp:FromEmail` - Sender email address
- `Smtp:FromName` - Sender display name

**Secrets location:**
- Environment variables (recommended)
- appsettings.json (development only - contains default/demo values)
- User Secrets (development): Configured via `UserSecretsId` in `IronMonkey.AppHost.csproj` (ID: `5431a824-64d2-42b2-b043-b95a56fa7784`)

## Webhooks & Callbacks

**Incoming:**
- Email invitation callback - Implicit in `IronMonkey.ApiService/Common/Services/EmailService.cs`
  - Invitation link passed to users via email
  - No webhook return path configured

**Outgoing:**
- None configured - No external webhooks for event notifications

## API Clients

**Internal Service Communication:**
- ApiClient in `IronMonkey.Web/ApiClient.cs` for Blazor Web → API Service communication
  - HTTP client configured with service discovery
  - Base address: `https+http://apiservice` (service discovery scheme)
  - Transport: HTTPS preferred over HTTP
  - Resilience: Polly handlers applied by default via `AddStandardResilienceHandler()`

## Multi-Tenancy

**Tenant Configuration:**
- Multi-tenant architecture via `BaseTenantEntity` base class
- Database connection per tenant: Optional `DatabaseConnectionString` field in `Tenant` entity
- Theme settings: Per-tenant customization via `ThemeSettings` field

---

*Integration audit: 2026-03-19*
