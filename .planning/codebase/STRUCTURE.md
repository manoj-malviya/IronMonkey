# Codebase Structure

**Analysis Date:** 2026-03-19

## Directory Layout

```
IronMonkey/
├── IronMonkey.AppHost/              # Aspire orchestration & service startup
│   ├── AppHost.cs                   # Orchestrates API and Web services
│   ├── IronMonkey.AppHost.csproj
│   └── appsettings.json
│
├── IronMonkey.ApiService/           # REST API backend
│   ├── Program.cs                   # Entry point, service configuration
│   ├── Endpoints.cs                 # Endpoint registration & routing
│   ├── ConfigureServices.cs         # DI and service setup
│   ├── Authentication/              # Auth-related endpoints
│   │   ├── Endpoints/               # IEndpoint implementations
│   │   │   ├── CreateTenant.cs
│   │   │   ├── CreateUser.cs
│   │   │   ├── CreateRole.cs
│   │   │   ├── ListRoles.cs
│   │   │   ├── CreatePermission.cs
│   │   │   ├── ListRolePermissions.cs
│   │   │   └── AttachPermissionsToRole.cs
│   │   └── Services/
│   │       └── Registration.cs
│   ├── Common/                      # Cross-cutting concerns
│   │   ├── IEndpoint.cs             # Interface for all endpoints
│   │   ├── Auth/                    # Authorization & user context
│   │   │   ├── IUserContext.cs
│   │   │   ├── UserContext.cs
│   │   │   ├── PermissionAuthorizationHandler.cs
│   │   │   └── PermissionAuthorizationPolicyProvider.cs
│   │   ├── Extensions/              # Extension methods
│   │   │   ├── ClaimsPrincipalExtensions.cs
│   │   │   └── RouteHandlerBuilderValidationExtensions.cs
│   │   ├── Results/                 # Custom result types
│   │   │   └── ValidationError.cs
│   │   ├── Services/                # Business services
│   │   │   └── EmailService.cs
│   │   ├── Filters/                 # Endpoint filters
│   │   │   ├── RequestValidationFilters.cs
│   │   │   └── EnsureUserOwnsEntityFilter.cs
│   │   ├── Cache/
│   │   │   └── CacheService.cs
│   │   └── Email/                   # SMTP configuration
│   ├── appsettings.json
│   └── IronMonkey.ApiService.csproj
│
├── IronMonkey.Web/                  # Blazor Server frontend
│   ├── Program.cs                   # Entry point, Blazor configuration
│   ├── ApiClient.cs                 # HTTP client for API communication
│   ├── Components/                  # Blazor components
│   │   ├── App.razor                # Root component
│   │   ├── Routes.razor
│   │   ├── Layout/
│   │   │   ├── MainLayout.razor
│   │   │   ├── MainLayout.razor.css
│   │   │   ├── NavMenu.razor
│   │   │   └── NavMenu.razor.css
│   │   ├── Pages/
│   │   │   ├── Home.razor
│   │   │   ├── Counter.razor
│   │   │   └── Error.razor
│   │   ├── SuperAdmin/              # Admin-only components
│   │   │   ├── CreatePermission.razor
│   │   │   ├── CreateRoleAndAssignPermission.razor
│   │   │   ├── ListPermissions.razor
│   │   │   └── ListRoles.razor
│   │   ├── _Imports.razor
│   │   └── wwwroot/                 # Static assets
│   ├── appsettings.json
│   └── IronMonkey.Web.csproj
│
├── IronMonkey.Data/                 # Entity Framework & persistence
│   ├── AppDbContext.cs              # Database context
│   ├── Seed.cs                      # Database seeding
│   ├── Entities/                    # Domain entities
│   │   ├── Tenant.cs
│   │   ├── User.cs
│   │   ├── Role.cs
│   │   ├── Permission.cs
│   │   ├── RolePermission.cs
│   │   ├── Contact.cs
│   │   ├── Lead.cs
│   │   └── Opportunity.cs
│   ├── Abstractions/                # Base entity classes & interfaces
│   │   ├── Entity.cs                # Base with audit tracking
│   │   ├── BaseTenantEntity.cs      # Multi-tenant base
│   │   ├── IEntity.cs
│   │   └── IDomainEvent.cs
│   ├── Configurations/              # EF Fluent API configurations
│   │   ├── TenantConfiguration.cs
│   │   ├── UserConfiguration.cs
│   │   ├── RoleConfiguration.cs
│   │   ├── PermissionConfiguration.cs
│   │   ├── RolePermissionConfiguration.cs
│   │   ├── ContactConfiguration.cs
│   │   ├── LeadConfiguration.cs
│   │   ├── OpportunityConfiguration.cs
│   │   └── OutboxMessageConfiguration.cs
│   ├── Extensions/
│   │   └── ConfigureDatabase.cs     # Service configuration
│   ├── Migrations/                  # EF migrations
│   │   ├── 20260126160318_Initial.cs
│   │   ├── 20260126160318_Initial.Designer.cs
│   │   └── AppDbContextModelSnapshot.cs
│   ├── Outbox/
│   │   └── OutboxMessage.cs         # Event sourcing outbox pattern
│   └── IronMonkey.Data.csproj
│
├── IronMonkey.Common/               # Shared types & utilities
│   ├── Auth/
│   │   ├── Jwt.cs                   # JWT token generation & validation
│   │   ├── JwtOptions.cs
│   │   ├── LoggedInUser.cs          # User DTO from token
│   │   └── RoleConstants.cs
│   ├── Exceptions/
│   │   └── ConcurrencyException.cs
│   ├── UserType.cs
│   └── IronMonkey.Common.csproj
│
├── IronMonkey.ServiceDefaults/      # Aspire service defaults
│   └── IronMonkey.ServiceDefaults.csproj
│
├── IronMonkey.sln                   # Solution file
├── README.md                        # Project documentation
└── .git/                            # Git repository
```

## Directory Purposes

**IronMonkey.AppHost:**
- Purpose: Service orchestration and startup configuration
- Contains: Aspire app host configuration, multi-service setup
- Key files: `AppHost.cs` orchestrates API and Web services with health checks

**IronMonkey.ApiService:**
- Purpose: RESTful API service for business operations
- Contains: Endpoints, request/response handlers, authentication, authorization, validation
- Key patterns: Minimal Endpoints with IEndpoint interface, fluent validation, endpoint filters

**IronMonkey.Web:**
- Purpose: Web frontend using Blazor Server
- Contains: Razor components, layouts, pages
- Communication: HttpClient via `ApiClient` to IronMonkey.ApiService

**IronMonkey.Data:**
- Purpose: Data persistence and entity definitions
- Contains: EF Core DbContext, domain entities, configurations, migrations
- Key feature: Outbox pattern for domain event handling, soft deletes, multi-tenancy support

**IronMonkey.Common:**
- Purpose: Shared utilities, authentication, exceptions
- Contains: JWT token handling, domain event interface, custom exceptions
- Used by: All projects for common types

**IronMonkey.ServiceDefaults:**
- Purpose: Aspire configuration defaults for services
- Contains: Service defaults shared across AppHost orchestration

## Key File Locations

**Entry Points:**
- `IronMonkey.AppHost/AppHost.cs`: Orchestrates and starts all services
- `IronMonkey.ApiService/Program.cs`: API service configuration, middleware setup
- `IronMonkey.Web/Program.cs`: Web frontend configuration, Blazor setup

**Configuration:**
- `IronMonkey.ApiService/appsettings.json`: JWT key, SMTP, database connection
- `IronMonkey.Web/appsettings.json`: Logging, API service URL
- `IronMonkey.Data/Extensions/ConfigureDatabase.cs`: Database service registration

**Core Logic:**
- `IronMonkey.ApiService/Endpoints.cs`: Master endpoint registration
- `IronMonkey.ApiService/ConfigureServices.cs`: DI, validation, auth, logging setup
- `IronMonkey.Data/AppDbContext.cs`: Database context with SaveChanges override for domain events

**Database:**
- `IronMonkey.Data/AppDbContext.cs`: DbContext definition
- `IronMonkey.Data/Migrations/`: EF Core migrations
- `IronMonkey.Data/Entities/`: Domain model classes

**Authentication & Authorization:**
- `IronMonkey.Common/Auth/Jwt.cs`: Token generation and validation
- `IronMonkey.ApiService/Common/Auth/UserContext.cs`: Current user context extraction
- `IronMonkey.ApiService/Common/Auth/PermissionAuthorizationHandler.cs`: Authorization logic

**Validation:**
- `IronMonkey.ApiService/Common/Filters/RequestValidationFilters.cs`: Request validation filter
- `IronMonkey.ApiService/Authentication/Endpoints/*.cs`: Each endpoint has nested RequestValidator

## Naming Conventions

**Files:**
- Endpoints: `[Action].cs` (e.g., `CreateUser.cs`, `CreateTenant.cs`, `ListRoles.cs`)
- Entities: `[EntityName].cs` (e.g., `User.cs`, `Tenant.cs`, `Lead.cs`)
- Services: `[ServiceName]Service.cs` (e.g., `EmailService.cs`, `CacheService.cs`)
- Configurations: `[EntityName]Configuration.cs` (e.g., `UserConfiguration.cs`)
- Handlers: `[Name]Handler.cs` (e.g., `PermissionAuthorizationHandler.cs`)
- Filters: `[Purpose]Filter.cs` (e.g., `RequestValidationFilters.cs`)

**Directories:**
- Feature-based grouping: `/Authentication`, `/Common`, `/Entities`
- Cross-cutting concerns: `/Common` subdirectories for `Auth`, `Services`, `Extensions`
- Data layer: `/Configurations` for EF mappings, `/Entities` for domain models, `/Abstractions` for base classes

**Classes:**
- Sealed where possible (e.g., `sealed class User`, `sealed class CreateUser`)
- PascalCase for class names
- Private parameterless constructors for ORM (EF Core)
- Factory static methods: `Create()` (e.g., `User.Create()`, `Tenant.Create()`)

**Endpoint Requests/Responses:**
- Records for DTO pairs nested in endpoint class
- Request/Response pattern: `public record Request(...)` and `public record Response(...)`
- Validators as nested inner classes: `public class RequestValidator : AbstractValidator<Request>`

**Properties:**
- Private setters for entity properties unless mutable
- Init-only for immutable properties (e.g., `public Guid Id { get; init; }`)
- Private backing fields for collections (e.g., `private readonly List<Role> _roles = new()`)

## Where to Add New Code

**New Feature Endpoint:**
1. Create endpoint class in `IronMonkey.ApiService/[Feature]/Endpoints/[Action].cs`
2. Implement `IEndpoint` interface with static `Map()` method
3. Define nested `Request` and `Response` record types
4. Define nested `RequestValidator : AbstractValidator<Request>`
5. Implement private static `Handle()` method with dependency injection
6. Register in `IronMonkey.ApiService/Endpoints.cs` in appropriate `MapGroup()`

**New Domain Entity:**
1. Create entity in `IronMonkey.Data/Entities/[EntityName].cs`
2. Inherit from `Entity` for standalone or `BaseTenantEntity` for multi-tenant
3. Use sealed classes, private setters, factory `Create()` method
4. Create configuration in `IronMonkey.Data/Configurations/[EntityName]Configuration.cs`
5. Add DbSet in `AppDbContext.cs` if new top-level entity

**New Service:**
1. Create interface in `IronMonkey.ApiService/Common/Services/I[ServiceName].cs`
2. Create implementation in `IronMonkey.ApiService/Common/Services/[ServiceName].cs`
3. Register in `ConfigureServices.cs` with appropriate lifetime (Scoped/Singleton/Transient)

**New Authorization/Authentication Logic:**
1. For handlers: Create in `IronMonkey.ApiService/Common/Auth/[Name]Handler.cs`
2. Register in `ConfigureServices.AddAuthorization()`
3. For user context: Extend `IUserContext` interface and `UserContext` implementation

**Utilities & Extensions:**
1. Extension methods: `IronMonkey.ApiService/Common/Extensions/[Name]Extensions.cs`
2. Shared utilities: `IronMonkey.Common/` for cross-project use

## Special Directories

**`IronMonkey.Data/Migrations/`:**
- Purpose: EF Core-generated migration files
- Generated: Yes (by dotnet ef migrations add)
- Committed: Yes (migrations tracked in version control)

**`IronMonkey.Data/Outbox/`:**
- Purpose: Domain event persistence for transactional outbox pattern
- Generated: No
- Committed: Yes

**`IronMonkey.Web/Components/wwwroot/`:**
- Purpose: Static assets (CSS, JS, images)
- Generated: No
- Committed: Yes

**`.planning/codebase/`:**
- Purpose: Architecture and structure documentation
- Generated: Yes (by GSD tools)
- Committed: Yes

**`bin/` and `obj/`:**
- Purpose: Build output and intermediate compilation files
- Generated: Yes (by dotnet build)
- Committed: No (.gitignore)

---

*Structure analysis: 2026-03-19*
