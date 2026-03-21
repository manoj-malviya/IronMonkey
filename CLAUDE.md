# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

IronMonkey is a multi-tenant CRM system built with .NET 10.0, .NET Aspire, and PostgreSQL. It uses database-per-tenant isolation with a central database for tenant metadata. Currently in active development — Phase 1 (multi-tenancy foundation) is complete, Phase 2 (configurable lead model) is in progress.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build IronMonkey.sln

# Run via Aspire orchestration (starts PostgreSQL, API service)
dotnet run --project IronMonkey.AppHost

# Run API service directly
dotnet run --project IronMonkey.ApiService

# Run all tests (requires Docker for Testcontainers)
dotnet test IronMonkey.Tests

# Run a single test by name
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests.Approved_tenant_can_be_provisioned"

# Run tests by class
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests"

# EF Core migrations (central database)
dotnet ef migrations add <Name> --project IronMonkey.Data --startup-project IronMonkey.ApiService --context CentralDbContext --output-dir Migrations/Central

# EF Core migrations (tenant database)
dotnet ef migrations add <Name> --project IronMonkey.Data --startup-project IronMonkey.ApiService --context TenantDbContext --output-dir Migrations/Tenant
```

## Architecture

### Multi-Tenant Data Isolation
- **CentralDbContext** — Stores `Tenant`, `SignupRequest`, `UserTenantIndex`, outbox messages. Single shared database.
- **TenantDbContext** — Per-tenant isolated database with `User`, `Lead`, `Contact`, `Opportunity`, `Role`, `Permission`. Created during tenant provisioning.
- **TenantDbContextFactory** — Creates TenantDbContext instances for a specific tenant's connection string.
- Global query filters on TenantDbContext enforce `TenantId` and `!IsDeleted` automatically.

### API Layer (IronMonkey.ApiService)
- **Minimal API** endpoints implement `IEndpoint` interface with static `Map()` method.
- Endpoints are registered in `Endpoints.cs` via `MapEndpoint<T>()` extension.
- Services are registered in `ConfigureServices.cs` via `builder.AddServices()`.
- Request/Response DTOs are nested records inside endpoint classes.
- Validators are nested classes (e.g., `CreateUser.RequestValidator`) using FluentValidation (globally imported).
- Typed results pattern: `Results<Ok<Response>, ValidationError, NotFound>`.

### Authentication Flow
1. `LoginEndpoint` looks up email in `UserTenantIndex` (central DB) for O(1) tenant resolution.
2. Loads user from tenant DB, verifies BCrypt password.
3. JWT token includes `tenant_id` claim. `IUserContext` extracts current user/tenant from claims.

### Background Jobs
- Hangfire with PostgreSQL storage, queues: `default`, `tenant`.
- Outbox pattern: domain events saved as `OutboxMessage` in `SaveChangesAsync`, processed by Hangfire.

### Aspire Orchestration (IronMonkey.AppHost)
- PostgreSQL container + `CentralDb` database.
- API service uses Aspire service discovery (`https+http://apiservice`).
- Web frontend (Blazor Server) exists but is currently commented out in AppHost.

## Key Conventions

- **C# 14 extension members**: `extension(WebApplicationBuilder builder) { ... }` syntax used in `ConfigureServices.cs` and `Endpoints.cs`.
- **Entity factory methods**: Use `Entity.Create(...)` static factory pattern, not constructors.
- **File-scoped namespaces**: `namespace IronMonkey.ApiService.Authentication.Endpoints;`
- **Namespace pattern**: `IronMonkey.[Project].[Feature]` (e.g., `IronMonkey.Data.Entities`).
- **Private fields**: `_camelCase` with underscore prefix.
- **No linter/formatter config**: Relies on standard C# conventions and nullable reference type checking.

## Testing

- **Framework**: xUnit 2.9.3 with Moq 4.20.72.
- **Integration tests** use Testcontainers (`postgres:15-alpine`) via shared `PostgreSqlFixture`.
- **Docker required**: Tests spin up real PostgreSQL containers — no mocked databases.
- Tests are in `IronMonkey.Tests/Integration/` and `IronMonkey.Tests/Unit/`.

## Dependencies to Note

- **EF Core 10.0.5** with **Npgsql 10.0.1** (must use matching major versions).
- **Aspire 13.1.0** for orchestration and service defaults.
- **Hangfire** for background job processing.
- **Serilog** for structured logging.
