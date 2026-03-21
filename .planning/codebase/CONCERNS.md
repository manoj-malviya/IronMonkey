# Codebase Concerns

**Analysis Date:** 2026-03-19

## Critical Syntax Errors

**Extension Method Declaration Error:**
- Issue: Malformed extension method syntax in `Endpoints.cs` and `ConfigureServices.cs`
- Files:
  - `IronMonkey.ApiService/Endpoints.cs` (line 28)
  - `IronMonkey.ApiService/ConfigureServices.cs` (line 20)
- Impact: Code will not compile. Extensions are declared with invalid syntax: `extension(Type param)` instead of `public static void MethodName(this Type param)`
- Fix approach: Convert malformed extension methods to proper C# syntax with explicit `public static` keywords

**Code Sample (BROKEN):**
```csharp
// Endpoints.cs line 28 - INCORRECT
extension(IEndpointRouteBuilder app)
{
    private void MapHealthCheckEndpoints()
```

**Correct Pattern:**
```csharp
public static void MapEndpoints(this WebApplication app)
{
    private void MapHealthCheckEndpoints()
```

## Password Storage Security Issue

**Plaintext Password Storage:**
- Issue: User passwords are stored as plaintext strings in the database without hashing
- Files:
  - `IronMonkey.Data/Entities/User.cs` (properties line 20-21)
  - `IronMonkey.ApiService/Authentication/Endpoints/CreateUser.cs` (line 43)
- Impact: Major security vulnerability. Compromised database exposes all user passwords in plaintext
- Fix approach:
  - Implement password hashing using BCrypt, PBKDF2, or Argon2
  - Update `User.Create()` to hash passwords before storage
  - Update login validation to compare hashed values
  - Never store plaintext passwords in the entity

## JWT Token Expiry Issue

**Extended Token Lifetime:**
- Issue: JWT tokens issued with 1-year expiration in `IronMonkey.Common/Auth/Jwt.cs` (line 31)
- Impact: Tokens remain valid for an entire year, creating security risk if token is compromised. Recommended expiry is 15 minutes to 1 hour
- Fix approach:
  - Reduce JWT expiration to appropriate duration (e.g., 15 minutes)
  - Implement refresh token pattern for long-lived sessions
  - Store refresh tokens in database with expiry tracking

## Authorization Query N+1 Problem

**Inefficient Permission Loading:**
- Issue: `AuthorizationService.GetPermissionsForUserAsync()` (line 53-56) loads all roles for a user, then all permissions for those roles
- Files: `IronMonkey.ApiService/Common/Auth/AuthorizationService.cs`
- Impact: Multiple database queries per authorization check; performance degrades with many roles/permissions
- Fix approach:
  - Use single LINQ query with proper joins to fetch permissions directly
  - Cache at HttpRequest level to avoid repeated queries
  - Consider implementing dedicated permission query with `SelectMany` on first query

**Current Code (Inefficient):**
```csharp
ICollection<Permission> permissions = await _dbContext.Set<User>()
    .Where(u => u.IdentityId == identityId)
    .SelectMany(u => u.Roles.Select(r => r.Permissions))
    .FirstAsync();
```

## Unsafe CORS Configuration

**Open CORS Policy:**
- Issue: CORS policy allows requests from any origin without restriction
- Files: `IronMonkey.ApiService/ConfigureServices.cs` (lines 140-148)
- Impact: Cross-Origin attacks possible; any website can make requests to this API
- Fix approach:
  - Whitelist specific allowed origins in configuration
  - Load allowed origins from appsettings.json
  - Restrict methods and headers appropriately

**Current Code:**
```csharp
policy.AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod();
```

## Email Service SSL Disabled

**Unencrypted SMTP Communication:**
- Issue: SMTP client in `EmailService.cs` (line 28) has SSL commented out: `// EnableSsl = true,`
- Files: `IronMonkey.ApiService/Common/Services/EmailService.cs`
- Impact: Email credentials and content transmitted in plaintext over network; vulnerable to eavesdropping
- Fix approach:
  - Enable SSL/TLS for SMTP connections
  - Make SSL requirement configurable but default to enabled
  - Validate SMTP certificate chain

## Missing Error Handling

**Unhandled Exception in GetPermissionsForUserAsync:**
- Issue: `GetPermissionsForUserAsync()` does not handle case where user has no roles (`.FirstAsync()` throws if empty)
- Files: `IronMonkey.ApiService/Common/Auth/AuthorizationService.cs` (line 56)
- Impact: API crashes with unhandled exception if user exists but has no roles assigned
- Fix approach:
  - Use `FirstOrDefaultAsync()` instead of `FirstAsync()`
  - Handle null/empty collection case explicitly
  - Return empty permission set for users without roles

## No Async Await in Void Method

**Fire-and-Forget Registration Task:**
- Issue: `RegisterAsContractor()` in `Registration.cs` (line 7) is declared `async void` but no await or proper async handling
- Files: `IronMonkey.ApiService/Authentication/Services/Registration.cs`
- Impact: Method doesn't actually execute asynchronously; exceptions cannot be caught; fire-and-forget pattern is dangerous
- Fix approach:
  - Change return type to `Task` instead of `void`
  - Call with `await` at call site
  - Add proper exception handling

## Inconsistent Naming Conventions

**Mixed Casing in Method Names:**
- Issue: Method names use both camelCase and PascalCase inconsistently
- Files: `IronMonkey.ApiService/ConfigureServices.cs` (lines 33-34)
- Impact:
  ```csharp
  builder.addApiVersioning();  // camelCase - incorrect
  builder.addCors();           // camelCase - incorrect
  ```
- Fix approach: All extension methods should be PascalCase: `builder.AddApiVersioning()` and `builder.AddCors()`

## Commented-Out Authentication Configuration

**Dead Code Paths:**
- Issue: Large blocks of commented Identity and authentication configuration code
- Files: `IronMonkey.ApiService/ConfigureServices.cs` (lines 78-94)
- Impact:
  - Unclear which authentication approach is actually in use
  - Maintenance burden; code becomes stale
  - Suggests incomplete migration or abandoned features
- Fix approach:
  - Remove commented code entirely or move to documentation
  - Clarify which auth strategy is active (custom JWT vs ASP.NET Identity)
  - Add configuration comments explaining design decisions

## Outbox Pattern TypeNameHandling Security Issue

**Unsafe JSON Deserialization:**
- Issue: `AppDbContext.cs` (line 86) uses `TypeNameHandling.All` for JSON serialization of domain events
- Files: `IronMonkey.Data/AppDbContext.cs`
- Impact: JSON deserialization can be exploited to instantiate arbitrary .NET types (gadget chain attacks)
- Fix approach:
  - Use `TypeNameHandling.Objects` or `TypeNameHandling.Arrays` with validation
  - Implement custom serialization that validates type names against whitelist
  - Consider alternative event storage without type metadata

**Current Code:**
```csharp
private static readonly JsonSerializerSettings JsonSerializerSettings = new()
{
    TypeNameHandling = TypeNameHandling.All  // UNSAFE
};
```

## Test Coverage Gaps

**No Test Files Found:**
- Issue: No test projects or test files (*.Tests or *.Tests.csproj) detected in solution
- Impact:
  - Authentication logic (JWT, permissions, claims transformation) completely untested
  - Database operations lack integration tests
  - API endpoints have no validation of contract or behavior
  - Refactoring risks high due to lack of regression detection
- Priority: **High** - Critical path code requires comprehensive test coverage

## Unused/Disabled Endpoint Features

**Commented Authorization Filters:**
- Issue: `RouteHandlerBuilderValidationExtensions.cs` has commented-out entity ownership and existence validators
- Files: `IronMonkey.ApiService/Common/Extensions/RouteHandlerBuilderValidationExtensions.cs` (lines 28-59)
- Impact:
  - Authorization decorators not implemented
  - Endpoints may expose data cross-tenant or to unauthorized users
  - API lacks multi-tenancy enforcement
- Fix approach: Uncomment and implement entity authorization filters, or remove if not needed

## Connection String Management

**Hardcoded SQLite Connection:**
- Issue: Database connection uses `DefaultConnection` from configuration with no fallback
- Files: `IronMonkey.Data/Extensions/ConfigureDatabase.cs`
- Impact:
  - SQLite acceptable for development but unsuitable for production multi-tenant application
  - No connection pooling configuration for production scenarios
  - Migration path to SQL Server/PostgreSQL unclear
- Fix approach:
  - Implement production database provider selection logic
  - Add connection pooling configuration
  - Plan migration strategy from SQLite

## Seed Data Generation Disabled

**No Role/Permission Initialization:**
- Issue: `Seed.cs` has all generation code commented out
- Files: `IronMonkey.Data/Seed.cs` (lines 8-21)
- Impact:
  - No initial roles (Admin, Contractor, Writer) created on database initialization
  - API endpoints depend on pre-existing roles but nothing bootstraps them
  - Manual database seeding required before first use
- Fix approach: Uncomment and refactor seed logic to run on first database migration

## Missing Email Configuration Validation

**No Configuration Validation:**
- Issue: `SmtpSettings` class has no validation; missing required settings cause runtime NullReferenceExceptions
- Files: `IronMonkey.ApiService/Common/Services/EmailService.cs` (line 76-83)
- Impact:
  - Missing SMTP credentials cause crashes at runtime, not at startup
  - No clear error messages if configuration is incomplete
  - Services are configured but fail silently until email is sent
- Fix approach:
  - Add data validation annotations to `SmtpSettings`
  - Add `ValidateOnStart` to service configuration
  - Add logging for configuration issues during startup

## Incomplete API Client

**Empty ApiClient Implementation:**
- Issue: `IronMonkey.Web/ApiClient.cs` is empty - only has constructor
- Files: `IronMonkey.Web/ApiClient.cs`
- Impact:
  - Web project cannot communicate with API service
  - No methods to call API endpoints
  - Placeholder suggests incomplete feature
- Fix approach: Implement HTTP methods for API calls or document if not yet needed

## Disabled CORS in Pipeline

**Commented CORS Middleware:**
- Issue: `app.UserCors();` is commented out in `Program.cs` (line 31)
- Files: `IronMonkey.ApiService/Program.cs`
- Impact: CORS policy is configured but never applied to request pipeline
- Fix approach: Uncomment or remove; decide if CORS is needed

## Domain Events Not Wired

**Event Handlers Not Implemented:**
- Issue: `User.RaiseDomainEvent()` is commented out; no event handler infrastructure
- Files: `IronMonkey.Data/Entities/User.cs` (line 30)
- Impact:
  - Domain event pattern is partially implemented but non-functional
  - Events are stored in outbox but never processed
  - No event handler middleware or background worker
- Fix approach:
  - Implement event handler dispatcher
  - Add background job processor for outbox events
  - Or remove event infrastructure if not needed

## Cache Invalidation Not Implemented

**No Cache Invalidation Strategy:**
- Issue: Authorization cache is set but never invalidated when roles/permissions change
- Files: `IronMonkey.ApiService/Common/Auth/AuthorizationService.cs`
- Impact:
  - Role/permission changes are not visible to users until cache expires (distributed memory cache has no TTL set)
  - Granting/revoking access is delayed indefinitely
- Fix approach:
  - Set explicit cache expiration times
  - Implement cache invalidation when roles/permissions are modified
  - Consider event-driven cache invalidation

## Multi-Tenancy Incomplete

**Missing Tenant Isolation Enforcement:**
- Issue:
  - `BaseTenantEntity` suggests multi-tenancy support
  - No middleware to extract tenant from request
  - No tenant-scoped DbContext filtering
  - Queries don't filter by tenant automatically
- Files: `IronMonkey.Data/Abstractions/BaseTenantEntity.cs`
- Impact: Tenants can potentially access each other's data
- Fix approach:
  - Implement tenant context middleware
  - Add automatic tenant filtering to EF Core queries via `IAsyncQueryableFilter`
  - Validate tenant ID in all mutations

---

*Concerns audit: 2026-03-19*
