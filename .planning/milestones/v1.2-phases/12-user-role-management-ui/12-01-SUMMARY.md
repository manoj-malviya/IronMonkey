---
phase: 12-user-role-management-ui
plan: 01
subsystem: user-management-api
tags: [api, users, tenant-scoped, bcrypt, endpoints]
dependency_graph:
  requires: []
  provides: [user-management-api-endpoints]
  affects: [IronMonkey.ApiService, IronMonkey.Data]
tech_stack:
  added: []
  patterns: [tenant-scoped-endpoint, bcrypt-password-generation, ignore-query-filters]
key_files:
  created:
    - IronMonkey.ApiService/Features/UserManagement/ListUsersEndpoint.cs
    - IronMonkey.ApiService/Features/UserManagement/GetUserEndpoint.cs
    - IronMonkey.ApiService/Features/UserManagement/CreateTenantUserEndpoint.cs
    - IronMonkey.ApiService/Features/UserManagement/UpdateUserEndpoint.cs
    - IronMonkey.ApiService/Features/UserManagement/DeactivateUserEndpoint.cs
    - IronMonkey.ApiService/Features/UserManagement/ResetPasswordEndpoint.cs
  modified:
    - IronMonkey.Data/Entities/User.cs
    - IronMonkey.ApiService/Endpoints.cs
decisions:
  - "IgnoreQueryFilters used on all user queries to include deactivated (IsDeleted=true) users — global query filter excludes them by default"
  - "16-byte RandomNumberGenerator.Fill + Convert.ToBase64String yields ~24-char password — sufficient entropy for temporary credentials"
  - "Duplicate MapIngestionEndpoints removed from Endpoints.cs — pre-existing duplicate that caused CS0111 compilation error"
metrics:
  duration_minutes: 15
  completed_date: "2026-04-01"
  tasks_completed: 3
  files_changed: 8
---

# Phase 12 Plan 01: User Management API Endpoints Summary

**One-liner:** Tenant-scoped CRUD endpoints for user management with BCrypt password generation and soft-delete via ITenantDbContextFactory pattern.

## What Was Built

Six Minimal API endpoints for tenant-scoped user management plus 4 mutation methods on the User entity:

### New Endpoints (IronMonkey.ApiService/Features/UserManagement/)

| Endpoint | Method | Route | Auth |
|----------|--------|-------|------|
| ListUsersEndpoint | GET | /user-management/users | Required |
| GetUserEndpoint | GET | /user-management/users/{id} | Required |
| CreateTenantUserEndpoint | POST | /user-management/users | Required |
| UpdateUserEndpoint | PUT | /user-management/users/{id} | Required |
| DeactivateUserEndpoint | DELETE | /user-management/users/{id} | Required |
| ResetPasswordEndpoint | POST | /user-management/users/{id}/reset-password | Required |

### User Entity Mutations (IronMonkey.Data/Entities/User.cs)

- `Update(string name, string email)` — updates name/email with timestamp
- `UpdateRole(Role newRole)` — clears and replaces role with timestamp
- `Deactivate()` — sets IsDeleted, DeletedAt, UpdatedAt
- `ResetPassword(string hashedPassword)` — replaces hashed password with timestamp

### Patterns Applied

All endpoints follow established tenant-scoped pattern:
```csharp
var tenantId = tenantService.GetCurrentTenantId();
var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
```

Password generation uses 16-byte cryptographic random + BCrypt hash:
```csharp
var bytes = new byte[16];
RandomNumberGenerator.Fill(bytes);
var plaintext = Convert.ToBase64String(bytes);
var hashed = BC.HashPassword(plaintext);
```

All list/get/update/deactivate queries use `IgnoreQueryFilters()` to bypass the global `IsDeleted` filter and include deactivated users in results.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed duplicate MapIngestionEndpoints method**
- **Found during:** Task 2 (verification build)
- **Issue:** `Endpoints.cs` contained two identical `MapIngestionEndpoints` methods causing CS0111 compilation error. This was a pre-existing duplicate — identical in content, added during Phase 3-5 development.
- **Fix:** Removed the second duplicate (lines 182-202) — the first method (lines 138-157) is unchanged and correct.
- **Files modified:** `IronMonkey.ApiService/Endpoints.cs`
- **Commit:** 8f805d3

### Out-of-Scope Pre-existing Issues

`IronMonkey.Web` has 10+ build errors in SuperAdmin Blazor components (`ApiClient.GetAsync`, `ApiClient.PostAsync`, `JSRuntimeExtensions` usage). These pre-exist this plan and are NOT caused by these changes. Logged to deferred-items for Phase 12 UI plans to address.

## Commits

| Hash | Message |
|------|---------|
| 83944bb | feat(12-01): add mutation methods to User entity |
| 8f805d3 | feat(12-01): create 6 tenant-scoped user management endpoints |
| 025b7e1 | feat(12-01): register user management endpoints in Endpoints.cs |

## Known Stubs

None — all endpoints are fully implemented with real database operations against TenantDbContext.

## Self-Check: PASSED

- IronMonkey.ApiService/Features/UserManagement/ListUsersEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/UserManagement/GetUserEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/UserManagement/CreateTenantUserEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/UserManagement/UpdateUserEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/UserManagement/DeactivateUserEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/UserManagement/ResetPasswordEndpoint.cs: FOUND
- IronMonkey.Data/Entities/User.cs mutation methods: FOUND
- IronMonkey.ApiService/Endpoints.cs endpoint registrations: FOUND
- dotnet build IronMonkey.ApiService: 0 errors
