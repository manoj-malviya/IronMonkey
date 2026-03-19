# Architecture

**Analysis Date:** 2026-03-19

## Pattern Overview

**Overall:** Layered Architecture with Minimal Endpoints Pattern

**Key Characteristics:**
- Clean separation between API service and web frontend
- Endpoint-based routing using ASP.NET Core's Minimal APIs
- Domain-driven design with entity models and value objects
- Multi-tenant architecture with tenant isolation at entity level
- Event sourcing via outbox pattern for domain events
- Fluent Validation for request validation at endpoint level

## Layers

**Presentation Layer (Frontend):**
- Purpose: Web UI for user interaction with the system
- Location: `IronMonkey.Web`
- Contains: Blazor components (.razor files), layouts, pages
- Depends on: IronMonkey.ApiService (via HttpClient)
- Used by: End users via browser

**API Layer:**
- Purpose: HTTP endpoints for business operations
- Location: `IronMonkey.ApiService/Authentication/Endpoints`, `IronMonkey.ApiService/Common`
- Contains: Endpoint implementations, request/response DTOs, validators
- Depends on: IronMonkey.Data (for DbContext), IronMonkey.Common (for shared types)
- Used by: IronMonkey.Web frontend and external clients

**Domain/Business Logic Layer:**
- Purpose: Core CRM entities and business rules
- Location: `IronMonkey.Data/Entities`, `IronMonkey.Data/Abstractions`
- Contains: Entity classes (Tenant, User, Lead, Contact, Opportunity, Role, Permission), factory methods, domain logic
- Depends on: None (pure domain)
- Used by: API layer, Data layer

**Data Access Layer:**
- Purpose: Database operations and persistence
- Location: `IronMonkey.Data`
- Contains: Entity Framework DbContext, configurations, migrations
- Depends on: Domain entities
- Used by: API endpoints, services

**Infrastructure Layer:**
- Purpose: Cross-cutting concerns and external integrations
- Location: `IronMonkey.Common`, `IronMonkey.ServiceDefaults`, `IronMonkey.AppHost`
- Contains: JWT authentication, authorization handlers, email service, caching, logging
- Depends on: Domain types
- Used by: API and Web services

## Data Flow

**User Creation Flow:**

1. POST request to `/users` endpoint reaches `CreateUser` handler
2. Request validated via `RequestValidationFilter<CreateUser.Request>` using FluentValidator
3. Role lookup from database via `AppDbContext.Set<Role>()`
4. Domain entity created: `User.Create(tenantId, name, email, password, role)`
5. Domain events raised within entity (not yet published)
6. Entity persisted: `dbContext.Set<User>().Add(user)` and `SaveChangesAsync()`
7. SaveChanges triggers `AppDbContext.SaveChangesAsync()` override which:
   - Collects domain events from changed entities
   - Converts domain events to `OutboxMessage` objects
   - Persists outbox messages and entities in transaction
   - Timestamps auto-generated on entity
8. Response returned: `Ok<Response>` with UserId and message

**Tenant Creation Flow:**

1. POST request to `/tenant` with Name, Slug, SubscriptionPlan, Status
2. Request validation via FluentValidator
3. Uniqueness check: `db.Set<Tenant>().AnyAsync(x => x.Name == request.Name)`
4. Entity factory: `Tenant.Create(name, slug, subscriptionPlan, status)` generates new Guid
5. Persistence and outbox mechanism same as user creation

**Authentication Flow:**

1. User authentication generates JWT token via `Jwt.GenerateToken(LoggedInUser)`
2. JWT claims include NameIdentifier (IdentityId), Email, Name, Role
3. Per-request: `IUserContext` extracts UserId and IdentityId from claims via `ClaimsPrincipalExtensions`
4. Authorization policies evaluated via `PermissionAuthorizationPolicyProvider` and `PermissionAuthorizationHandler`
5. Custom claims transformation adds custom claims to principal via `CustomClaimsTransformation`

**State Management:**

- Transient entities: Injected once per request (e.g., `Jwt` service)
- Scoped services: Created once per HTTP request (e.g., `IUserContext`, `IEmailService`)
- Singleton services: Created once for application lifetime (e.g., `ICacheService`)
- Distributed memory cache: In-process caching via `DistributedMemoryCache`
- Database context: Scoped per request, transaction per SaveChanges call

## Key Abstractions

**IEndpoint:**
- Purpose: Defines contract for all endpoints in Minimal APIs
- Examples: `CreateUser : IEndpoint`, `CreateTenant : IEndpoint`, `CreateRole : IEndpoint`
- Pattern: Static abstract `Map(IEndpointRouteBuilder app)` method registers route

**Entity:**
- Purpose: Base class for all domain entities with audit tracking
- Examples: `User`, `Tenant`, `Lead`, `Contact`, `Opportunity`
- Pattern: Tracks CreatedAt, UpdatedAt, DeletedAt, IsDeleted; maintains domain events in `_domainEvents` collection

**BaseTenantEntity:**
- Purpose: Base class for multi-tenant domain entities
- Examples: `User`, `Lead`, `Contact`, `Opportunity`
- Pattern: Extends Entity, adds TenantId for tenant isolation

**IDomainEvent:**
- Purpose: Marker interface for domain events
- Pattern: Raised by entities via `RaiseDomainEvent()`, collected in SaveChangesAsync, stored as OutboxMessage

**IUserContext:**
- Purpose: Provides current user information in request scope
- Implementation: `UserContext` extracts UserId and IdentityId from HttpContext.User claims
- Pattern: Injected as scoped dependency

**Request/Response Records:**
- Purpose: DTO pairs for endpoint inputs/outputs
- Examples: `CreateUser.Request`, `CreateTenant.Response`
- Pattern: Nested public record classes within endpoint class, validators as inner classes

## Entry Points

**AppHost Orchestrator:**
- Location: `IronMonkey.AppHost/AppHost.cs`
- Triggers: dotnet run from AppHost project
- Responsibilities: Orchestrates services using .NET Aspire, health checks, service configuration

**API Service Program:**
- Location: `IronMonkey.ApiService/Program.cs`
- Triggers: Aspire AppHost launches IronMonkey.ApiService
- Responsibilities: Configures services via `builder.AddServices()`, sets up middleware, maps endpoints via `app.Configure()`

**Web Service Program:**
- Location: `IronMonkey.Web/Program.cs`
- Triggers: Aspire AppHost launches IronMonkey.Web (currently commented out in AppHost)
- Responsibilities: Configures Blazor components, sets up HttpClient for API communication

**Endpoint Registration:**
- Location: `IronMonkey.ApiService/Endpoints.cs`
- Triggers: Called from `app.Configure()` in Program.cs
- Responsibilities: Maps all endpoint groups: /health, /auth, /users, /tenant, /user-management

## Error Handling

**Strategy:** Problem Details + Custom ValidationError result type

**Patterns:**

- **Validation Errors:** `ValidationError` result type (custom IResult implementation) returns 400 with HttpValidationProblemDetails
- **Not Found:** `TypedResults.NotFound()` returns 404
- **Database Concurrency:** `AppDbContext.SaveChangesAsync()` catches `DbUpdateConcurrencyException` and throws `ConcurrencyException` with context
- **Soft Deletes:** Entity.IsDeleted flag prevents hard deletes; records marked with DeletedAt timestamp
- **Exception Handler Middleware:** `app.UseExceptionHandler()` in API catches unhandled exceptions

## Cross-Cutting Concerns

**Logging:**
- Serilog configured in `ConfigureServices.AddSerilog()` via appsettings.json configuration
- Used in request validation filter: logs validation attempts and results
- Per-request logging via ILogger<T> injection

**Validation:**
- FluentValidation via `builder.Services.AddValidatorsFromAssembly(typeof(ConfigureServices).Assembly)`
- Per-endpoint validators nested in endpoint class (e.g., `CreateUser.RequestValidator`)
- Applied via `RequestValidationFilter<TRequest>` endpoint filter and `WithRequestValidation<TRequest>()` extension

**Authentication:**
- JWT bearer authentication configured in `AddJwtAuthentication()`
- Token validation: IssuerSigningKey, lifetime validation, clock skew 0
- Token generation: 1-year expiry, claims include NameIdentifier, Email, Name, Role
- Location: `IronMonkey.Common/Auth/Jwt.cs` and `IronMonkey.ApiService/Common/Auth/UserContext.cs`

**Authorization:**
- Role-based and permission-based via `PermissionAuthorizationPolicyProvider` and `PermissionAuthorizationHandler`
- Custom claims transformation via `CustomClaimsTransformation`
- Endpoints marked with `.RequireAuthorization()` or `.AllowAnonymous()`

**Caching:**
- Distributed memory cache via `DistributedMemoryCache`
- `ICacheService` singleton provides cache access
- Location: `IronMonkey.ApiService/Common/Cache/CacheService.cs`

**Email:**
- SMTP configuration via `SmtpSettings` from appsettings
- `IEmailService` scoped service in `IronMonkey.ApiService/Common/Services/EmailService.cs`
- Configured in `ConfigureServices.AddEmailServices()`

---

*Architecture analysis: 2026-03-19*
