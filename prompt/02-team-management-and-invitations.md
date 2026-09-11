# Team Management And Invitations

## Context

The current User Management page lists users, creates users directly, edits them, and deactivates them. It does not provide a tenant-friendly invitation lifecycle. Team administration is a central requirement for onboarding a tenant.

## Prompt

Extend tenant team management into a complete, auditable invitation and user lifecycle workflow.

### Requirements

- Add an invite flow that accepts name, email, role, and optional message. Do not require the Admin to choose or transmit a password for another person.
- Add invitation states: pending, accepted, expired, revoked, and failed. Display state, sent date, expiration, and the most relevant next action.
- Support resend invitation, revoke invitation, and copy invitation link where the security model allows it. Resending must rotate the token and expiration rather than reuse an old token.
- Add an acceptance route that validates a single-use, tenant-bound, expiring token and lets the invited user set their password. Prevent accepting the same token twice or accepting it for a different email/tenant.
- Keep existing direct-create behavior only if it is needed for backward compatibility; make the safe invitation path the primary action.
- Add search/filter by name, email, role, active state, and invitation state. Add pagination or a clear scalable loading strategy.
- Provide password reset, deactivate/reactivate, and role-change flows with confirmation and audit visibility. Prevent an Admin from accidentally removing the last active Admin.
- Show useful loading, empty, validation, conflict, unauthorized, and server-error states. Avoid exposing whether an arbitrary email exists outside the current tenant.
- Keep all data and endpoints tenant-scoped and use `AdminApiClient` from Blazor pages.

### Acceptance criteria

- An Admin can invite a teammate, see the pending state, resend or revoke it, and the invited user can accept exactly once.
- Expired, revoked, already-used, mismatched-tenant, and mismatched-email tokens are rejected without account creation.
- A role change takes effect on the next authenticated request and is recorded in an audit trail.
- The last active tenant Admin cannot be deactivated or demoted without a safe replacement.
- Tests cover tenant isolation, token expiry/single use, duplicate email handling, permissions, and the last-Admin guard.

### UI direction

- Keep the list scannable with status badges and row actions grouped in an overflow menu on narrow screens.
- Use a modal or dedicated form for invite/create; do not compress the whole workflow into an inline table row.
- Make destructive actions visually distinct and require a confirmation that names the affected user.

### Likely implementation surfaces

- `IronMonkey.Web/Components/Pages/Admin/Users/UserList.razor`
- `IronMonkey.Web/Components/Pages/Admin/Users/UserCreate.razor`
- `IronMonkey.Web/Components/Pages/Admin/Users/UserEdit.razor`
- Tenant user/authentication endpoints and corresponding entities/services
- Email/outbox infrastructure and focused integration tests