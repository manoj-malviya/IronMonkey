# Coding Conventions

**Analysis Date:** 2026-03-19

## Naming Patterns

**Files:**
- Class files: PascalCase (e.g., `CreateUser.cs`, `EmailService.cs`, `UserConfiguration.cs`)
- Exception files: PascalCase (e.g., `ConcurrencyException.cs`)
- Test files: Not yet implemented
- Interface files: PascalCase with I prefix (e.g., `IEndpoint.cs`, `IEmailService.cs`, `IUserContext.cs`)
- Extension files: Suffix with "Extensions" (e.g., `RouteHandlerBuilderValidationExtensions.cs`, `ConfigureDatabase.cs`)

**Classes:**
- Entity classes: PascalCase (e.g., `User`, `Role`, `Tenant`, `Permission`)
- Service classes: PascalCase with Service suffix (e.g., `EmailService`, `CacheService`, `AuthorizationService`)
- Configuration classes: PascalCase with "Configuration" suffix (e.g., `UserConfiguration`, `RoleConfiguration`)
- Endpoint classes: PascalCase matching action (e.g., `CreateUser`, `CreateRole`, `ListRoles`)
- Validator classes: PascalCase with "Validator" suffix, nested within endpoint class (e.g., `CreateUser.RequestValidator`)
- Static classes used for extensions: PascalCase (e.g., `Endpoints`, `ConfigureServices`, `ConfigureApp`)

**Functions/Methods:**
- Public methods: PascalCase (e.g., `Map()`, `Handle()`, `Create()`, `SendWriterInvitationAsync()`)
- Private methods: PascalCase (e.g., `GenerateTimestamps()`, `AddDomainEventsAsOutboxMessages()`)
- Static factory methods: `Create()` pattern (e.g., `User.Create()`, `Role.Create()`, `Tenant.Create()`)
- Async methods: PascalCase with Async suffix (e.g., `SendWriterInvitationAsync()`, `SaveChangesAsync()`)
- Extension methods: Part of extension class (e.g., `MapEndpoints()`, `Configure()`, `WithRequestValidation<T>()`)

**Variables:**
- Local variables: camelCase (e.g., `toEmail`, `publisherName`, `mailMessage`, `client`)
- Parameters: camelCase (e.g., `request`, `dbContext`, `builder`, `connectionString`)
- Private fields: camelCase with underscore prefix (e.g., `_logger`, `_smtpSettings`, `_domainEvents`, `_roles`)
- Constants: PascalCase (e.g., `SuperAdmin`, `Admin`, `Owner`, `TeleCaller`)
- QueryString/Config keys: camelCase (e.g., `defaultPolicy`)

**Types/Records/Classes:**
- Records: PascalCase (e.g., `Request`, `Response`, `LoggedInUser`, `JwtOptions`)
- Enums: PascalCase (e.g., `UserType`)
- Interfaces: I prefix with PascalCase (e.g., `IEndpoint`, `IEmailService`, `IEntity`, `IDomainEvent`)
- Generic type parameters: Single capital letter or descriptive PascalCase (e.g., `T`, `TRequest`, `TResponse`, `TEntity`, `TEndpoint`)

**Namespaces:**
- Pattern: `IronMonkey.[ProjectName].[Feature]`
- Examples: `IronMonkey.ApiService.Authentication.Endpoints`, `IronMonkey.ApiService.Common.Services`, `IronMonkey.Data.Entities`, `IronMonkey.Data.Configurations`
- Top-level utility namespaces: `IronMonkey.Common.Auth`, `IronMonkey.Common.Exceptions`

## Code Style

**Formatting:**
- No explicit formatter configuration detected. Code follows standard C# formatting conventions.
- Line length: Not constrained in examined code (ranges from 50 to 150+ characters)
- Indentation: 4 spaces (standard C#)
- Braces: Opening brace on same line (Allman style not used)

**Nullable Reference Types:**
- Enabled globally across all projects via `<Nullable>enable</Nullable>` in `.csproj` files
- Required properties marked with `required` keyword (e.g., `public required string Key { get; init; }` in `JwtOptions`)
- Nullable properties explicitly marked with `?` (e.g., `string? message`, `string? DatabaseConnectionString`)
- Implicit usings enabled: `<ImplicitUsings>enable</ImplicitUsings>`

**Linting:**
- No `.editorconfig` or linting configuration detected
- Code relies on C# compiler warnings and nullable reference type checking
- StyleCop configuration not present

**Init-Only Properties:**
- Used for data integrity in configuration objects (e.g., `public required string Key { get; init; }`)
- Used in record types for immutability (e.g., `public record Request(Guid TenantId, ...)`)
- Role constants use init: `public int Id { get; init; }`, `public string Name { get; init; }`

**Record Types:**
- Used extensively for request/response DTOs within endpoint classes
- Declared as positional records for concise syntax (e.g., `public record Request(Guid TenantId, string Name, ...)`)
- Nested within endpoint class for logical grouping

## Import Organization

**Order:**
1. System namespaces (e.g., `using System;`, `using System.Net.Mail;`, `using System.Collections.Generic;`)
2. Microsoft namespaces (e.g., `using Microsoft.AspNetCore.Http;`, `using Microsoft.EntityFrameworkCore;`)
3. Third-party/NuGet namespaces (e.g., `using FluentValidation;`, `using Newtonsoft.Json;`, `using Serilog;`)
4. Internal project namespaces (e.g., `using IronMonkey.Data;`, `using IronMonkey.ApiService.Common;`)

**Global Usings:**
- `FluentValidation` added globally via `global using FluentValidation;` in `IronMonkey.ApiService/Program.cs`
- Allows direct use of validation interfaces without explicit imports in validators

**Path Aliases:**
- No custom path aliases detected (e.g., no @/ or ~/ style aliases)
- Full namespace paths used throughout

**File-scoped Namespaces:**
- All files use file-scoped namespace syntax: `namespace IronMonkey.ApiService.Authentication.Endpoints;` (no braces)

## Error Handling

**Patterns:**
- Try-catch wrapping at service/integration layers (e.g., in `EmailService.SendWriterInvitationAsync()`)
- Logging exceptions before re-throwing: `_logger.LogError(ex, "Failed to send writer invitation email to {Email}", toEmail);`
- Custom exception types for domain-specific errors: `ConcurrencyException` for EF concurrency conflicts
- Validation errors returned as typed results in minimal APIs: `new ValidationError("Role already exists.")`
- DbUpdateConcurrencyException caught and transformed to domain exception in `AppDbContext.SaveChangesAsync()`
- Endpoints return typed result tuples: `Results<Ok<Response>, ValidationError, NotFound>` for clear success/error states
- NotFound results: `TypedResults.NotFound()` for missing resources
- Success results: `TypedResults.Ok(new Response(...))` for successful operations
- No try-catch in endpoints themselves (rely on middleware and typed results)

**Exception Handling in DbContext:**
- Override `SaveChangesAsync()` to catch EF exceptions and wrap in domain exceptions
- Prevents leaking EF-specific exceptions to application layers
- Example: `DbUpdateConcurrencyException` → `ConcurrencyException` (see `IronMonkey.Data/AppDbContext.cs`)

## Logging

**Framework:** Serilog

**Configuration:**
- Added via `builder.AddSerilog()` in `IronMonkey.ApiService/ConfigureServices.cs`
- Request logging middleware: `app.UseSerilogRequestLogging()`
- Configuration read from appsettings via `configuration.ReadFrom.Configuration(context.Configuration)`

**Patterns:**
- Injected as `ILogger<T>` in services (e.g., `ILogger<EmailService> _logger`)
- Structured logging with named parameters: `_logger.LogError(ex, "Failed to send writer invitation email to {Email}", toEmail)`
- Logging at service/integration boundaries (e.g., email failures, database errors)
- No application-level logging in endpoints (expected to be handled by request logging middleware)

## Comments

**When to Comment:**
- XML documentation comments (JSDoc-style) for public methods and interfaces
- Summary tags used for methods: `/// <summary>Adds a request validation filter...</summary>`
- Parameter documentation in complex extension methods
- Inline comments sparse (found only in commented-out code blocks)

**JSDoc/TSDoc:**
- Uses XML documentation: `/// <summary>`, `/// <typeparam>`, `/// <param>`, `/// <returns>`
- Example from `RouteHandlerBuilderValidationExtensions.cs`:
  ```csharp
  /// <summary>
  /// Adds a request validation filter to the route handler.
  /// </summary>
  /// <typeparam name="TRequest"></typeparam>
  /// <param name="builder"></param>
  /// <returns>A <see cref="RouteHandlerBuilder"/> that can be used to futher customize the endpoint.</returns>
  public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
  ```
- Used extensively in extension methods and public APIs
- Not consistently applied to all public members

## Function Design

**Size:**
- Generally compact (10-50 lines)
- Service methods moderate length due to business logic (50-75 lines for email formatting)
- Static factory methods very short (5-10 lines)

**Parameters:**
- Minimal parameters per function (1-3 typical)
- Use dependency injection for services rather than parameters
- Request objects passed as single parameter to endpoints (promotes loose coupling)
- Async/await used consistently for I/O operations

**Return Values:**
- Explicit typed results in minimal APIs: `Task<Results<Ok<Response>, ValidationError>>`
- Tuples with multiple possible outcomes (success/validation error/not found)
- Void for setup/configuration methods
- Entity methods use static factory pattern returning constructed instance

**Method Chaining:**
- Fluent API pattern used for endpoint configuration:
  ```csharp
  app.MapPost("/users", Handle)
      .WithSummary("Creates a new user in the system")
      .WithRequestValidation<Request>();
  ```
- Extension methods return builder for continuation

## Module Design

**Exports:**
- All public types in namespace are exported (no explicit export restrictions detected)
- Static classes used for extension methods (convention over configuration)
- Services exposed via interfaces (e.g., `IEmailService`, `ICacheService`)

**Barrel Files/Re-exports:**
- No barrel files (index.cs) detected
- Direct imports from module namespaces required

**Folder Structure as Modules:**
- `Authentication/` - Auth-related endpoints and services
- `Common/` - Shared utilities, extensions, auth helpers, caching, services
- `Data/` - Entity Framework contexts, migrations, configurations
- Entities grouped in single `Entities/` folder
- Configurations in separate `Configurations/` folder

## Dependencies

**Dependency Injection:**
- Service registration in `ConfigureServices.cs` using extension method on `WebApplicationBuilder`
- DI container used for all services: `IEmailService`, `AppDbContext`, `ICacheService`, `Jwt`
- Scoped services: `IUserContext`, `AuthorizationService`
- Transient services: `Jwt`, `IClaimsTransformation`, `IAuthorizationHandler`
- Singletons: `ICacheService`, configuration options
- Factory registrations for complex services

**Pattern for Service Configuration:**
- Extension method on builder (e.g., `AddServices()`, `AddJwtAuthentication()`)
- Each concern isolated in separate method
- Clear separation between service registration and configuration

---

*Convention analysis: 2026-03-19*
