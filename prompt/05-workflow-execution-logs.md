# Workflow Execution Logs

## Context

Workflow rules are stored per tenant and evaluated asynchronously through Hangfire. Lead changes dispatch a `WorkflowRuleEvaluationJob`, which loads the tenant database and calls `WorkflowRuleEngine`. The engine currently writes diagnostics through `ILogger` only: it logs evaluation errors, invalid actions, webhook responses, and the fact that email is not delivered. Those logs are useful to developers but are not durable, tenant-scoped, or visible to an Admin trying to debug an automation.

## Prompt

Implement a tenant-visible workflow execution history so an Admin can see when a workflow was triggered, what happened, and why an action succeeded or failed.

## Where to store the logs

Store user-facing execution records in a new `WorkflowExecutionLog` table in each tenant database, managed by `TenantDbContext` and included in tenant migrations. This is the correct boundary because the records contain tenant-owned lead, rule, user, and action data, and database-per-tenant isolation is already the system’s primary data-isolation model.

Do not store tenant workflow history only in application files, console logs, Hangfire tables, or the central database:

- `ILogger` output is ephemeral and is not queryable from the CRM UI.
- Hangfire storage is appropriate for job state, retries, and infrastructure operations, not the tenant’s business audit history.
- The central database should not receive tenant lead details or become a cross-tenant data leak risk.

Keep infrastructure diagnostics in `ILogger` and Hangfire. The tenant execution record is the durable business-facing history; both layers may be written for the same execution.

## Requirements

### Durable execution model

- Add a tenant-scoped `WorkflowExecutionLog` entity and `DbSet`.
- Capture at least: execution id, tenant id, workflow rule id, workflow rule name snapshot, lead id, trigger type, status, started-at, completed-at, duration, error category, error message safe for Admin display, and correlation/job id.
- Track the action-level result, not only the overall rule result. An execution should be able to show condition evaluated, condition matched/skipped, action type, action status, action start/end, HTTP status for webhooks where applicable, recipient redaction for email, and a safe diagnostic message.
- Use statuses that distinguish `Queued`, `Running`, `Skipped`, `Succeeded`, `Failed`, and `PartiallySucceeded` or an equivalent explicit state model.
- Preserve the rule name and action type as snapshots so historical rows remain understandable after a rule is renamed or deleted.
- Never persist passwords, webhook secrets, authorization headers, full email bodies containing sensitive data, or unrestricted lead custom-field payloads. Redact URLs/query strings and sensitive headers before storing diagnostics.
- Define a retention policy and cleanup mechanism. Make the default retention configurable, index the tenant/rule/lead/time/status query paths, and avoid unbounded growth.

### Execution semantics

- Create the execution record when the background evaluation starts, then update it as the rule and each action progresses. Ensure a failed action records its failure before the worker completes.
- Record condition mismatch as an intentional `Skipped` result with a reason such as “condition did not match”; do not treat it as an error.
- Record malformed condition/action JSON, unknown action types, invalid webhook URLs, non-success webhook responses, timeout/network failures, missing email recipients, and unsupported email delivery as distinct diagnostic categories.
- Preserve the current behavior that a failing rule/action does not roll back the lead change or prevent other rules from being evaluated. Make that behavior visible in the log.
- Make retries idempotent. A Hangfire retry must not create confusing duplicate success records for one logical trigger. Either reuse a correlation/execution id or clearly link retry attempts to one parent execution.
- Handle cancellation and process failure with a recoverable state such as `Abandoned`/`TimedOut`, or provide a reconciliation job that marks stale `Running` rows after a threshold.
- Continue writing structured `ILogger` events with tenant id, execution id, rule id, lead id, trigger, and action type for operator diagnostics.

### API and UI

- Add tenant-authorized endpoints to list execution logs with pagination, newest-first ordering, and filters for date range, status, rule, trigger, lead, and action type.
- Add a detail endpoint that returns the execution timeline and safe diagnostics. Do not return secrets or unrestricted action payloads.
- Add a “Workflow Runs” or “Execution History” view reachable from Workflow Rules/System Configuration. Keep it separate from the lead activity timeline while providing a link to the related lead.
- In the workflow rules list, show a recent-run count and latest status/time where practical. Add a rule-specific “View runs” action.
- In the detail view, show a readable timeline: trigger received, condition result, action started, action completed/failed, retry attempts, and final outcome. Include correlation id for support/debugging.
- Add empty, loading, partial-failure, unauthorized, and retention/older-than-available states. Provide a retry action for failed data loads, not an action that silently reruns a business workflow.
- Use `AdminApiClient` from Blazor pages and load after the first interactive render, following the existing authentication/prerender pattern.

### Security and tenancy

- Apply the existing tenant authorization rules. A tenant Admin may see only that tenant’s logs; a platform SuperAdmin must use the existing impersonation flow to access tenant-scoped history.
- Do not accept tenant id from the browser as an authority. Resolve it from the authenticated tenant context and apply the global tenant query filter.
- Avoid leaking whether another tenant has a matching lead, rule, email, or execution id through error messages or response timing.
- Consider a separate permission such as `workflow:logs:read` if the existing role model supports it; default Admin access should be explicit and tested.

## Acceptance criteria

- When a lead change triggers a workflow, an Admin can find a durable execution record showing trigger, rule, lead, condition result, action result, timestamps, and final status.
- A condition that does not match is visible as an intentional skip, not a silent absence or failure.
- Webhook success, non-2xx response, timeout, invalid URL, and email-not-configured cases are distinguishable without exposing secrets.
- If one action fails, the run explains the failure and whether later rules/actions continued; the original lead write remains successful.
- Hangfire retries do not produce misleading duplicate logical runs, and stale running executions are recoverable or clearly marked.
- Logs are isolated per tenant, paginated, retention-bounded, indexed, and covered by migration, API, workflow-engine, and authorization tests.

## Likely implementation surfaces

- `IronMonkey.Data/Entities/WorkflowRule.cs`
- `IronMonkey.Data/Entities/ActivityLog.cs` for comparison, not as a replacement
- `IronMonkey.Data/TenantDbContext.cs`
- A new tenant migration and `WorkflowExecutionLog` configuration
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/WorkflowRuleEngine.cs`
- `IronMonkey.ApiService/BackgroundJobs/WorkflowRuleEvaluationJob.cs`
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/WorkflowTriggerDispatcher.cs`
- New workflow execution log endpoints and Blazor pages/components
- Focused tests in `IronMonkey.Tests`

Do not make the UI a raw dump of server logs. It should present a safe, searchable business execution history, while retaining structured server logs for operators.