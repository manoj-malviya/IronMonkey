# Granular Permissions and Record Visibility

## Context

The permission model is a flat list of eighteen strings — `leads:read`, `leads:write`, `contacts:delete`, `admin:access` and so on — resolved from `role_permissions` for tenant users and from `PermissionConstants.ForPlatformRole` for platform users. It answers "may this user read leads", but not "*which* leads". `Lead.AssignedToUserId` and `LeadTask.AssignedToUserId` are the only ownership signals in the entire data model, and nothing filters on them: any user with `leads:read` reads every lead in the tenant.

That is wrong for most verticals. A dealership does not let one salesperson browse another's enquiries. A brokerage has regulatory reasons to segment books of business. A university keeps admissions and finance apart. Custom roles per tenant are listed as a Future requirement in `PROJECT.md`, and `Role` is currently informational — recipes seed role *names* with no permissions attached.

There is also no team or hierarchy concept, so "a manager sees their team's records" cannot be expressed at all.

## Prompt

Add record-level visibility and tenant-defined roles, without weakening the tenant isolation boundary that already works.

### Tenant-defined roles

- Let a tenant Admin create roles and assign permissions from the existing permission catalog, replacing today's informational roles.
- `admin:access` must remain ungrantable by a tenant. It is the platform gate, and the whole `/admin/*` surface depends on no tenant role holding it — `RecipeAdminPermissionTests` pins that invariant today. A tenant Admin granting itself `admin:access` would reach the platform catalog every other tenant provisions from. Enforce the exclusion server-side, not by omitting it from a dropdown.
- Likewise `SuperAdmin` (role 1) stays platform-only and must not be creatable, assignable or impersonatable from tenant-facing endpoints.
- Prevent a tenant from locking itself out: the last user holding tenant-administration rights must not be able to remove them from itself or be deleted.
- Permission changes must take effect predictably. State explicitly whether they apply on next request or next login, and make sure a revoked permission cannot outlive its revocation inside a long-lived Blazor Server circuit.

### Teams and hierarchy

- Add a tenant team/group model with membership and an optional manager relationship, deep enough to express "manager sees their reports' records" without inviting unbounded recursion.
- Define and enforce a maximum hierarchy depth, and reject cycles at write time. A cycle in a manager chain turns every visibility query into an infinite loop.

### Record visibility

- Introduce visibility scopes — at least *own*, *team*, and *all* — configurable per role and per record type, defaulting to today's behaviour (*all*) so no existing tenant silently loses access on upgrade.
- Enforce visibility **server-side in the query layer**, alongside the existing global tenant filter, not in the UI and not by post-filtering a materialized list. A UI-only rule is not a permission, and post-filtering leaks totals and paging metadata even when it hides rows.
- Make list pagination, sorting, counts and dashboard aggregates all respect visibility. `GET /api/leads` counts before paging so "1–25 of 240" is honest — that total must be honest about what the user may see, or the count itself discloses the hidden records.
- Cover the non-obvious read paths too: exports, reports, the activity timeline, workflow execution history, duplicate detection and merge, search, and lead assignment pickers. Duplicate detection is the subtle one — reporting "this lead already exists" against an invisible record discloses it.
- Define what happens when a user loses visibility of a record they own a task on, or are mid-edit of. An error is acceptable; silent data loss is not.
- Assignment and routing must still be able to target users the acting user cannot see records for, or round-robin and territory routing break under any scope narrower than *all*.

### Auditing

- Record permission and role changes in the activity/audit trail, including who changed what and when. Privilege changes are exactly what an auditor asks for, and the existing interceptor-driven `ActivityLog` is the natural home.

## Acceptance criteria

- A tenant Admin can define a role with a subset of permissions and a visibility scope, and a user in that role sees only the records the scope allows — through lists, detail routes, exports, reports, search and aggregates alike.
- Existing tenants upgrade with unchanged behaviour until an Admin narrows a scope.
- No tenant role can obtain `admin:access` or `SuperAdmin`, enforced server-side and covered by a test that fails if seed data ever reopens the hole.
- A tenant cannot remove its own last administrator.
- Direct navigation to a record id outside a user's visibility is refused, and the refusal does not disclose whether the record exists.
- List totals, paging and dashboard figures never count records the viewer may not see.
- Manager hierarchies resolve within a bounded depth, and a cycle is rejected at write time rather than hanging a query.
- Routing and assignment continue to work under a narrowed scope.
- Tests cover scope enforcement per record type, the hidden-record disclosure paths (duplicates, counts, aggregates, timeline), privilege-escalation attempts, lockout prevention, and hierarchy cycles.

## Likely implementation surfaces

- `IronMonkey.Common/Auth/PermissionConstants.cs`
- `IronMonkey.Data/Entities/Role.cs`, `RolePermission.cs`, `User.cs`, plus new team/membership entities
- `RolePermissionConfiguration` and a tenant migration
- `AuthorizationService` and `IUserContext`
- `IronMonkey.Data/TenantDbContext.cs` global query filters
- `IronMonkey.ApiService/Features/Leads/`, `Contacts/`, `Opportunities/`, `Reports/`, `Activity/`
- `IronMonkey.Web/Components/Pages/Admin/Users/`
- Tests in `IronMonkey.Tests`, including `RecipeAdminPermissionTests`

`User.Roles` is a computed property and is not queryable — query from `Roles` and traverse `Role.Users`. Visibility is a data-layer concern; every rule enforced only in markup is a bug.
