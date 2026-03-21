# Technology Stack

**Analysis Date:** 2026-03-19

## Languages

**Primary:**
- C# 13 - ASP.NET Core backend services and web application

## Runtime

**Environment:**
- .NET 10.0 - Target framework across all projects

**Package Manager:**
- NuGet - Official .NET package manager
- Lockfile: Present (packages.lock.json generated via NuGet)

## Frameworks

**Core:**
- ASP.NET Core 10.0.2 - Web framework for API and Blazor server
- Blazor Server Components - Interactive server-side rendering for `IronMonkey.Web`
- Minimal APIs - API endpoint routing pattern in `IronMonkey.ApiService`

**ORM & Data:**
- Entity Framework Core 10.0.2 - Object-relational mapping
  - SQLite provider (EF Core SQLite 10.0.2) - Primary database
  - In-Memory provider (EF Core InMemory 10.0.2) - Testing support
  - Identity EntityFrameworkCore 10.0.2 - User identity management

**Authentication & Authorization:**
- JWT Bearer Authentication - Token-based auth (`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.2)
- ASP.NET Core Identity - User/role management framework

**Validation:**
- FluentValidation 12.1.1 - Fluent validation framework
- FluentValidation.DependencyInjectionExtensions 12.1.1 - DI integration

**API Documentation & Versioning:**
- Swagger/OpenAPI 10.1.0 via Swashbuckle.AspNetCore
  - Swagger Gen 10.1.0
  - Swagger UI 10.1.0
  - Filters 10.0.1
- API Versioning 8.1.1 via Asp.Versioning
  - HTTP versioning support
  - MVC API Explorer integration

**Logging:**
- Serilog 10.0.0 - Structured logging framework (`Serilog.AspNetCore`)

**Serialization:**
- Newtonsoft.Json 13.0.3 - JSON serialization for domain events

**Observability & Monitoring:**
- OpenTelemetry 1.14.0 - Distributed tracing and metrics
  - OpenTelemetry.Exporter.OpenTelemetryProtocol 1.14.0 - OTLP exporter
  - OpenTelemetry.Extensions.Hosting 1.14.0 - Hosting integration
  - OpenTelemetry.Instrumentation.AspNetCore 1.14.0 - ASP.NET Core instrumentation
  - OpenTelemetry.Instrumentation.Http 1.14.0 - HTTP client instrumentation
  - OpenTelemetry.Instrumentation.Runtime 1.14.0 - Runtime metrics

**Distributed Systems:**
- .NET Aspire 13.1.0 - Distributed application orchestration
  - Aspire.AppHost.Sdk 13.1.0 - App orchestration host
- Service Discovery 10.1.0 via `Microsoft.Extensions.ServiceDiscovery`
- HTTP Resilience 10.1.0 via `Microsoft.Extensions.Http.Resilience`

## Key Dependencies

**Critical:**
- Microsoft.AspNetCore packages (10.0.2) - Core web framework
- Microsoft.EntityFrameworkCore packages (10.0.2) - Data persistence
- FluentValidation (12.1.1) - Input validation across API
- Serilog.AspNetCore (10.0.0) - Structured logging for diagnostics

**Infrastructure:**
- OpenTelemetry (1.14.0) - Observability backbone
- .NET Aspire (13.1.0) - Service orchestration and health checks
- Microsoft.Extensions.ServiceDiscovery (10.1.0) - Service mesh integration
- Microsoft.Extensions.Http.Resilience (10.1.0) - Circuit breakers, retries

## Configuration

**Environment:**
- Appsettings files: `appsettings.json` and `appsettings.Development.json`
- Configuration sources:
  - `IronMonkey.ApiService/appsettings.json` - JWT settings, SMTP, database connection
  - `IronMonkey.AppHost/appsettings.json` - AppHost logging configuration

**Build:**
- .csproj files with implicit usings and nullable reference types enabled
- Multi-project solution: `IronMonkey.sln` (6 projects)
- ServiceDefaults pattern for shared infrastructure configuration in `IronMonkey.ServiceDefaults/IronMonkey.ServiceDefaults.csproj`

## Platform Requirements

**Development:**
- Visual Studio 2017+ (Solution format: VS 17.8)
- .NET 10.0 SDK
- ASP.NET Core 10.0 runtime

**Production:**
- .NET 10.0 runtime (container-compatible)
- SQLite database (or alternate EF Core provider)
- OTLP endpoint (optional, for telemetry export)
- SMTP server (for email notifications)

## Project Structure

**IronMonkey.ApiService** - RESTful API service
- Target: net10.0, ASP.NET Core web SDK
- Key packages: FluentValidation, Swagger, JWT Auth, Entity Framework Core

**IronMonkey.Web** - Blazor Server frontend
- Target: net10.0, ASP.NET Core web SDK
- Features: Server-side rendering, output caching, HTTP client to API

**IronMonkey.Data** - Data access layer
- Target: net10.0, Standard class library
- Packages: EF Core, EF Core SQLite, EF Core InMemory, Identity EntityFrameworkCore

**IronMonkey.Common** - Shared utilities
- Target: net10.0, Standard class library
- Contains: JWT utilities, authentication models, exceptions

**IronMonkey.ServiceDefaults** - Shared infrastructure
- Target: net10.0, Aspire shared library
- Provides: OpenTelemetry, service discovery, health checks, resilience

**IronMonkey.AppHost** - Orchestration host
- Target: net10.0, Console application
- Orchestrates services via Aspire (currently only ApiService active)

---

*Stack analysis: 2026-03-19*
