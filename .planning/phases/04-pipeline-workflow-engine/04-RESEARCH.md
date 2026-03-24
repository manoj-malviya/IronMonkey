# Phase 4: Pipeline & Workflow Engine - Research

**Researched:** 2026-03-22
**Domain:** Lead lifecycle management, state machines, workflow automation, real-time UI updates
**Confidence:** HIGH

## Summary

Phase 4 implements a comprehensive pipeline and workflow management system for IronMonkey's CRM. The phase covers five distinct but interconnected domains: (1) **Kanban board UI** for visual lead management with optimistic drag-drop and validation; (2) **state machine transitions** enforcing configured allowed transitions between pipeline stages; (3) **task management** linking assignable work to leads; (4) **rule-based lead routing** via round-robin or territory-based assignment; and (5) **workflow rule engine** for trigger-based automation (field/status changes, time-elapsed).

The existing Phase 2 and 3 assets provide the foundation: `PipelineStage`, `Lead`, custom fields, duplicate detection, and Hangfire infrastructure. Phase 4 extends these with new entities (`Task`, `WorkflowRule`, `StageTransition`, `Notification`, `RoutingConfig`), new background jobs (workflow rule evaluation, hourly time-elapsed scanning), and Blazor Server UI components for the Kanban board and task/rule management.

**Primary recommendation:** Build the state machine and task infrastructure first (entities, endpoints, background jobs), then layer the Kanban UI and workflow rule engine on top. Use `Stateless` library for client-side state machine validation (not execution); store transitions as adjacency list in the database. Use Hangfire's scheduled jobs for hourly time-elapsed rule scanning. Implement optimistic Kanban drag-drop with explicit client-side snap-back on validation errors.

## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01 through D-08:** Kanban board behavior locked — optimistic drag-drop, virtual scroll per column (20 leads), basic filters (agent, source, date range), inline error on invalid transition
- **D-05 through D-08:** State machine configuration locked — linear by default with exception overrides, no default transitions (explicit config required), typed stages (Entry, Active, Closed-Won, Closed-Lost), inline error messages
- **D-09 through D-12:** Workflow rules engine locked — single trigger → single action, in-app notifications only, Hangfire hourly scan for time-elapsed, toggle on/off per rule
- **D-13 through D-16:** Lead routing locked — strict round-robin, tenant picks routing dimension (source or custom field), one strategy per pipeline, leave unassigned if no match

### Claude's Discretion

- Kanban board component architecture (Blazor Server components, SignalR real-time updates)
- Notification entity design and bell/badge UI
- Workflow rule evaluation order and conflict resolution
- Task entity schema and priority level definitions
- State machine transition storage format (adjacency list vs matrix)
- Round-robin pointer storage (per-pipeline counter in tenant DB)
- Hangfire job scheduling details

### Deferred Ideas (OUT OF SCOPE)

- Saved/named filter views — Phase 5+
- Email notifications — Phase 5+ (communications phase)
- Weighted/load-balanced routing — Phase 6+
- Priority-ordered routing rules with fallback chains — Phase 6+
- Workflow rule execution log/history — Phase 5+
- Chained conditions (AND/OR) on rules — Phase 6+
- Visual transition graph editor — Phase 6+

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PIPE-01 | User can view leads in Kanban-style pipeline board with drag-drop between stages | Kanban board component with optimistic updates, state machine validation layer |
| PIPE-02 | User can create tasks with due dates, priority, and assignment linked to leads | Task entity with FK to Lead and AssignedToUserId, task list UI |
| PIPE-03 | System supports manual lead assignment and rule-based routing (round-robin, territory) | RoutingConfig entity, ILeadRoutingService, routing evaluation at lead creation |
| PIPE-04 | Tenant can define workflow rules with triggers, conditions, and auto-actions | WorkflowRule entity, IWorkflowRuleEngine service, Hangfire background evaluation |
| PIPE-05 | Lead lifecycle follows configurable state machine with allowed transitions per tenant | StageTransition entity, state machine validation on lead.PipelineStageId update |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Stateless | 5.20.1 | Client-side lead state validation before drag-drop commit | Industry-standard state machine library; lightweight, no runtime dependencies; fluent API for building transition graphs |
| Hangfire.AspNetCore | 1.8.17 | Background job scheduling for workflow rule evaluation and time-elapsed trigger scanning | Already in use for outbox processing and CSV import; PostgreSQL storage; proven in production |
| Blazor Server | (built-in .NET 10) | Kanban board components, task management UI, workflow rule builder | Already in IronMonkey.Web project; matches Aspire architecture; real-time two-way data binding |
| Microsoft.AspNetCore.SignalR | 10.0.5 | Real-time Kanban board updates and task notifications (push-based, not polling) | Built-in to .NET 10; integrates with Blazor Server; low-latency multi-client synchronization |
| EntityFrameworkCore.PostgreSQL | 10.0.1 | ORM for Phase 4 entities in tenant database | Already in use; matches project version strategy; JSONB for storing transition matrices or rule definitions |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| RulesEngine | 5.1.2 | JSON-based rule condition evaluation (when, then, else logic) | Evaluate workflow rule conditions in a safe, expression-based manner without string parsing; integrates with Hangfire jobs |
| FluentValidation | 12.1.1 | Validate state transitions, task creation, rule definition | Already in use; consistent with existing endpoint validators |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Stateless | Custom adjacency list validation | Saves NuGet dependency but loses fluent API and battle-tested guard/condition logic; adjacency list alone insufficient for transition validation |
| RulesEngine | Manual expression parsing | Avoids dependency but loses type safety and raises injection vulnerability risks; RulesEngine handles sanitization |
| Hangfire for hourly scan | Clock.TickTimer in memory | Loses persistence across restarts; if process dies mid-scan, scheduled scans are lost. Hangfire recovery is automatic |
| Blazor components | React SPA in separate frontend | Adds complexity (separate TS/JS build, API contract drift); Blazor Server matches existing architecture |

**Installation:**
```bash
dotnet add package Stateless --version 5.20.1
dotnet add package RulesEngine --version 5.1.2
# Hangfire, SignalR, EF Core already present
```

**Version verification:**
- Stateless 5.20.1 (latest stable, March 2026)
- RulesEngine 5.1.2 (latest stable, March 2026)
- Hangfire.AspNetCore 1.8.17 (already in project)
- SignalR included in .NET 10.0.5 (already in project)

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.ApiService/
├── Features/Leads/
│   ├── Pipeline/
│   │   ├── Kanban/
│   │   │   ├── GetKanbanBoardEndpoint.cs        # Paginated board state
│   │   │   ├── MoveLeadEndpoint.cs              # Drag-drop with validation
│   │   │   └── GetLeadFiltersEndpoint.cs        # Supported filter dimensions
│   │   ├── States/
│   │   │   ├── StageTransitionConfiguration.cs  # Fluent Stateless setup
│   │   │   └── StateValidationService.cs        # Validate transition legality
│   │   ├── Routing/
│   │   │   ├── ConfigureRoutingEndpoint.cs      # Admin sets round-robin/territory strategy
│   │   │   ├── ILeadRoutingService.cs           # Assign on creation
│   │   │   └── LeadRoutingService.cs            # Round-robin pointer logic
│   │   └── Tasks/
│   │       ├── CreateTaskEndpoint.cs            # Create task linked to lead
│   │       ├── ListTasksEndpoint.cs             # Assignee's task list
│   │       └── UpdateTaskEndpoint.cs            # Update status, priority, due date
│   └── Workflow/
│       ├── Rules/
│       │   ├── CreateWorkflowRuleEndpoint.cs    # Admin defines trigger/condition/action
│       │   ├── ListWorkflowRulesEndpoint.cs     # Paginated list with IsActive toggle
│       │   ├── UpdateWorkflowRuleEndpoint.cs    # Modify rule
│       │   ├── IWorkflowRuleEngine.cs           # Evaluate triggers/conditions
│       │   └── WorkflowRuleEngine.cs            # Uses RulesEngine for expressions
│       └── Background/
│           ├── WorkflowRuleEvaluationJob.cs     # Field/status change triggers (event-driven via outbox)
│           ├── TimeElapsedRuleScanJob.cs        # Hourly scan for time-based triggers
│           └── NotificationDispatchJob.cs       # Fire actions (assign, notify, schedule)
├── BackgroundJobs/
│   ├── OutboxProcessingJob.cs                   # (existing, triggers workflow rules via domain events)
│   └── [TimeElapsedRuleScanJob]                 # (new)
└── Notifications/
    ├── INotificationService.cs
    └── NotificationService.cs                   # Create in-app notifications

IronMonkey.Data/
├── Entities/
│   ├── Task.cs                                  # Lead-linked task with due date, priority, assignee
│   ├── WorkflowRule.cs                          # Trigger + Condition (JSON) + Action (JSON)
│   ├── StageTransition.cs                       # From → To allowed transitions per tenant
│   ├── Notification.cs                          # In-app notification with read/unread
│   ├── RoutingConfig.cs                         # Round-robin pointer, routing dimension
│   └── [extend PipelineStage]                   # Add StageType enum (Entry, Active, ClosedWon, ClosedLost)
├── Migrations/Tenant/
│   └── [Phase 4 migration]                      # Add all Phase 4 DbSets
└── Configurations/
    ├── TaskConfiguration.cs
    ├── WorkflowRuleConfiguration.cs
    ├── StageTransitionConfiguration.cs
    ├── NotificationConfiguration.cs
    ├── RoutingConfigConfiguration.cs
    └── [extend PipelineStageConfiguration]

IronMonkey.Web/
├── Components/
│   ├── Pages/
│   │   └── Pipeline.razor                       # Main Kanban board page
│   └── Pipeline/
│       ├── KanbanBoard.razor                    # Kanban canvas with columns
│       ├── LeadCard.razor                       # Draggable card with lead info
│       ├── TaskList.razor                       # Assignee's tasks sidebar
│       ├── WorkflowRuleManager.razor            # Admin rule CRUD
│       └── RoutingConfigPanel.razor             # Admin routing setup
```

### Pattern 1: State Machine Configuration & Validation

**What:** Use Stateless library to build an in-memory state machine graph per tenant. Validate transitions before committing. Store allowed transitions in database; rebuild graph from DB at request time (or cache if perf needed).

**When to use:** Every lead drag-drop, every API call that moves a lead between stages.

**Example:**

```csharp
// Source: Stateless library pattern (https://github.com/dotnet-state-machine/stateless)
public class StateValidationService(ITenantDbContextFactory factory)
{
    public async Task<bool> IsTransitionAllowedAsync(
        Guid tenantId,
        Guid leadId,
        Guid targetStageId,
        CancellationToken cancellationToken)
    {
        var connStr = await _tenantService.GetConnectionStringAsync(tenantId, cancellationToken);
        await using var db = factory.CreateForTenant(connStr, tenantId);

        var lead = await db.Leads.FindAsync(new object[] { leadId }, cancellationToken);
        if (lead == null) return false;

        // Fetch allowed transitions from database
        var transitions = await db.StageTransitions
            .Where(t => t.TenantId == tenantId && t.FromStageId == lead.PipelineStageId)
            .Select(t => t.ToStageId)
            .ToListAsync(cancellationToken);

        return transitions.Contains(targetStageId);
    }
}

// In Kanban endpoint:
[HttpPost("leads/{leadId}/move")]
public async Task<Results<Ok, ValidationError>> MoveLeadAsync(
    Guid leadId, Guid targetStageId, IStateValidationService validator)
{
    if (!await validator.IsTransitionAllowedAsync(tenantId, leadId, targetStageId, ct))
    {
        return TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["targetStageId"] = ["Transition not allowed. Valid targets: Stage A, Stage B"]
            });
    }
    // Commit move...
}
```

### Pattern 2: Workflow Rule Evaluation (Event-Driven + Time-Based)

**What:** WorkflowRule has JSON for condition expression (RulesEngine format) and action payload (assign user, create task, notify). Field/status change triggers fire via outbox pattern (event emitted when Lead.PipelineStageId changes). Time-elapsed triggers scan hourly via Hangfire.

**When to use:** Any lead state change (trigger), or hourly for time-based rules.

**Example:**

```csharp
// Source: RulesEngine pattern (https://microsoft.github.io/RulesEngine/)
public class WorkflowRuleEngine : IWorkflowRuleEngine
{
    private readonly RulesEngine.RulesEngine _rulesEngine;

    public async Task<List<WorkflowAction>> EvaluateAsync(
        Lead lead, WorkflowRule rule, TenantDbContext db, CancellationToken ct)
    {
        var actions = new List<WorkflowAction>();

        // Parse rule condition (JSON) for RulesEngine
        var ruleSet = JsonConvert.DeserializeObject<RuleSet>(rule.ConditionJson);
        var ruleResult = await _rulesEngine.ExecuteAllRulesAsync(ruleSet, lead);

        if (ruleResult.All(r => r.IsSuccessful))
        {
            // Condition passed — execute action
            var action = JsonConvert.DeserializeObject<WorkflowAction>(rule.ActionJson);
            switch (action.Type)
            {
                case "assign":
                    lead.AssignedToUserId = action.UserId;
                    break;
                case "notify":
                    await _notificationService.NotifyAsync(action.UserId, action.Message);
                    break;
                case "schedule_task":
                    var task = Task.Create(lead.Id, action.TaskTitle, action.DueDate);
                    db.Tasks.Add(task);
                    break;
            }
            actions.Add(action);
        }

        return actions;
    }
}

// In outbox processing (Phase 4 extends Phase 1 OutboxProcessingJob):
// When LeadMovedToDifferentStageEvent is published, trigger workflow rule eval:
private async Task EvaluateWorkflowRulesAsync(Lead lead)
{
    var rules = await db.WorkflowRules
        .Where(r => r.TenantId == tenantId && r.IsActive && r.Trigger == "status_change")
        .ToListAsync();

    foreach (var rule in rules)
    {
        var actions = await _workflowEngine.EvaluateAsync(lead, rule, db);
        // Log actions to audit trail...
    }
}
```

### Pattern 3: Optimistic Kanban Drag-Drop with Rollback

**What:** Blazor component updates local state immediately (optimistic), sends MoveLeadEndpoint request in background. If response is 400 ValidationError, revert card position and show error toast. If success, keep card in new position.

**When to use:** Every drag-drop interaction on the board.

**Example:**

```razor
@* Source: Blazor drag-drop pattern from Syncfusion *@
<div class="kanban-board">
    @foreach (var stage in Stages)
    {
        <div class="kanban-column" @ondrop="(DropZoneEventArgs e) => OnDropAsync(e, stage.Id)"
             ondragover="event.preventDefault()">
            <h3>@stage.Name</h3>
            @foreach (var lead in stage.Leads)
            {
                <div class="lead-card" draggable="true"
                     @ondragstart="(DragEventArgs e) => OnDragStartAsync(e, lead)">
                    <p>@lead.FirstName @lead.LastName</p>
                    <p>@lead.Email</p>
                </div>
            }
        </div>
    }
</div>

@code {
    private LeadCard? draggingCard;
    private string? errorMessage;

    private void OnDragStartAsync(DragEventArgs e, Lead lead)
    {
        draggingCard = lead;
    }

    private async Task OnDropAsync(DropZoneEventArgs e, Guid targetStageId)
    {
        if (draggingCard == null) return;

        var originalStageId = draggingCard.PipelineStageId;

        // Optimistic update: move card immediately
        draggingCard.PipelineStageId = targetStageId;

        // Background validation
        try
        {
            await ApiClient.Post($"/leads/{draggingCard.Id}/move",
                new { targetStageId });
            // Success — card stays in new column
        }
        catch (ValidationException ex)
        {
            // Rollback: card snaps back
            draggingCard.PipelineStageId = originalStageId;
            errorMessage = ex.Message; // "Cannot move from [A] to [B]. Allowed: [C, D]"
            StateHasChanged();
        }
    }
}
```

### Pattern 4: Round-Robin Lead Routing

**What:** RoutingConfig stores current rotation pointer (next agent index) and routing dimension (source or custom field). On lead creation, if routing is enabled, assign lead to next agent in round-robin order, advance pointer, save.

**When to use:** During lead creation (CreateLeadEndpoint or any ingestion channel).

**Example:**

```csharp
// Source: Common scheduling pattern
public class LeadRoutingService : ILeadRoutingService
{
    public async Task<Guid?> GetNextAssigneeAsync(
        Guid tenantId, Guid pipelineId, Lead lead, TenantDbContext db)
    {
        var config = await db.RoutingConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PipelineId == pipelineId);

        if (config?.IsEnabled != true) return null;

        // Get agents who can be assigned (active users with agent role, filtered by dimension)
        var candidates = await GetCandidateAgentsAsync(tenantId, config, lead, db);
        if (!candidates.Any()) return null;

        // Round-robin: current pointer + 1, mod array length
        var nextIndex = (config.RoundRobinPointer + 1) % candidates.Count;
        var nextAgentId = candidates[nextIndex].Id;

        // Advance pointer in DB
        config.RoundRobinPointer = nextIndex;
        db.RoutingConfigs.Update(config);
        await db.SaveChangesAsync();

        return nextAgentId;
    }
}

// In CreateLeadEndpoint:
var assignedTo = await _routingService.GetNextAssigneeAsync(tenantId, pipelineId, newLead);
if (assignedTo.HasValue)
{
    newLead.AssignedToUserId = assignedTo.Value;
}
db.Leads.Add(newLead);
await db.SaveChangesAsync();
```

### Pattern 5: Hourly Time-Elapsed Rule Scan

**What:** Hangfire scheduled job runs every hour, queries all time-based workflow rules (trigger="time_elapsed"), evaluates conditions for all leads, fires matching actions. Uses ITenantRegistry to iterate all tenants.

**When to use:** Background processing, triggered by Hangfire scheduler, not by user action.

**Example:**

```csharp
// Source: Hangfire background job pattern (OutboxProcessingJob template)
public class TimeElapsedRuleScanJob
{
    private readonly ITenantRegistry _registry;
    private readonly ITenantDbContextFactory _factory;
    private readonly IWorkflowRuleEngine _engine;
    private readonly ILogger<TimeElapsedRuleScanJob> _logger;

    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]  // Uses default queue, not tenant-specific
    [RecurringJob("time-elapsed-rules", "0 * * * *")]  // Every hour at minute 0
    public async Task ScanTimeElapsedRulesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting hourly time-elapsed rule scan");

        // Iterate all tenants
        var tenants = await _registry.GetAllTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            var connStr = await _registry.GetConnectionStringAsync(tenant.Id, ct);
            await using var db = _factory.CreateForTenant(connStr, tenant.Id);

            // Get all active time-elapsed rules
            var rules = await db.WorkflowRules
                .Where(r => r.Trigger == "time_elapsed" && r.IsActive)
                .ToListAsync(ct);

            // For each rule, evaluate all leads
            var leads = await db.Leads.Where(l => !l.IsDeleted).ToListAsync(ct);
            foreach (var rule in rules)
            {
                foreach (var lead in leads)
                {
                    var actions = await _engine.EvaluateAsync(lead, rule, db, ct);
                    if (actions.Any())
                    {
                        _logger.LogInformation("Rule {RuleId} triggered for lead {LeadId}",
                            rule.Id, lead.Id);
                    }
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}

// In ConfigureServices.cs:
// Add RecurringJob registration (Hangfire will call ScanTimeElapsedRulesAsync every hour)
```

### Anti-Patterns to Avoid

- **Storing transition matrix as adjacency matrix in a single field:** Use StageTransition table instead — easier to query, compose, update. Matrix format locks you into row-based lookups and makes soft-deletes harder.
- **Eager-loading all workflow rules into memory at startup:** Rules can be added/modified at runtime by admins. Query DB each evaluation cycle. Cache only if tenant count is large (> 1000).
- **Synchronous workflow rule evaluation in the request path:** Block the move-lead request while evaluating 50 workflow rules = slow API. Emit domain event instead, let outbox job handle evaluation async.
- **Polling for Kanban board updates:** Use SignalR for real-time push. Polling defeats the "collaborative" aspect where two agents see each other's drag-drops.
- **Hard-coded round-robin logic per tenant:** Store rotation state in DB (RoutingConfig.RoundRobinPointer). If process restarts or scales horizontally, pointer survives.
- **Accepting free-form expressions in workflow rule conditions:** Always use RulesEngine or similar expression engine with sandboxing. Direct eval() is an injection vulnerability.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| State machine validation | Custom if-else cascades or switch statements for transition legality | Stateless library — builds fluent transition graph, logs violations, handles guards | Switch statements don't scale past 5-10 states; hard to visualize allowed transitions; Stateless handles hierarchical states if needed in future |
| Workflow rule condition evaluation | String parsing + regex + manual type casting | RulesEngine — JSON-based rules, type-safe expression parsing, injection-safe | Manual parsing is error-prone, vulnerable to expression injection; RulesEngine is battle-tested in production systems |
| Round-robin assignment | Track pointer in memory (volatile, lost on restart) | Database-backed RoutingConfig with atomic pointer increment | In-memory state disappears on process crash; horizontal scaling breaks pointer consistency |
| Real-time board sync | Poll API every 2 seconds | SignalR for push-based updates | Polling wastes bandwidth, adds latency, scales poorly; SignalR handles 10,000+ concurrent users efficiently |
| Drag-drop validation | Assume server accepts all moves, validate post-hoc | Validate state machine before committing, snap back card if invalid | Post-hoc validation confuses users ("why did my drag fail?"); pre-validation prevents bad state |
| Hourly rule scanning | Timer.Elapsed or Task.Delay loop in a singleton | Hangfire RecurringJob with ITenantRegistry iteration | Custom loop is not durable (lost on restart), not observable (no logging); Hangfire survives restarts, integrates with monitoring |

**Key insight:** Workflow automation looks simple ("if X then Y") but correctness requires audit trails, crash recovery, and state consistency. Use battle-tested libraries (Stateless, RulesEngine, Hangfire) rather than custom logic.

## Runtime State Inventory

> Not applicable — Phase 4 is not a rename/refactor phase. This is a greenfield feature phase.

## Common Pitfalls

### Pitfall 1: Transition Validation Skipped at Database Layer

**What goes wrong:** Endpoints validate transitions using Stateless library, but direct database updates (or bulk operations) bypass validation. Lead ends up in an invalid state (e.g., Closed-Won → Qualified). When Kanban board loads, state machine graph breaks because target stage doesn't exist or transition is forbidden.

**Why it happens:** Stateless validation lives in the API layer; database constraints don't enforce the state machine (only FK and basic rules).

**How to avoid:**
1. Add CHECK constraint in migration if using PostgreSQL: `CHECK (from_stage_id IS NULL OR exists(select 1 from pipeline_stages where id=from_stage_id))`
2. Always go through StateValidationService or MoveLeadEndpoint, never raw UPDATE lead.
3. In tests, verify that both direct DB inserts and API calls reject invalid transitions.

**Warning signs:** Tests pass but live system shows orphaned leads (PipelineStageId = null or non-existent), or "invalid transition" errors logged but not caught by validation.

### Pitfall 2: Workflow Rules Fire Multiple Times for Same Event

**What goes wrong:** Multiple workflow rules all have trigger="status_change", condition evaluates to true, same action fires twice. Lead gets assigned to two agents, or notification sent twice.

**Why it happens:** OutboxProcessingJob processes each OutboxMessage once, but a single lead update might trigger the same rule twice if not deduped.

**How to avoid:**
1. Track rule execution per lead per rule in an audit table (WorkflowRuleExecution: leadId, ruleId, firedAt).
2. In IWorkflowRuleEngine.EvaluateAsync, skip if already fired in last 60 seconds.
3. In tests, create multiple leads and rules, verify each rule fires exactly once per lead, even with parallel outbox processing.

**Warning signs:** Logs show "Rule X triggered" twice for single lead update, or users report duplicate assignments/notifications.

### Pitfall 3: Round-Robin Pointer Becomes Stale or Inconsistent

**What goes wrong:** Two concurrent requests both read RoundRobinPointer=0, both increment to 1, next two leads both assign to agent[1]. Rotation skips agents.

**Why it happens:** Read pointer, calculate next, write pointer is not atomic in EF Core SaveChangesAsync if two requests race.

**How to avoid:**
1. Use database-level atomic increment: `UPDATE routing_config SET round_robin_pointer = (round_robin_pointer + 1) % array_length WHERE id=...` returning the new value.
2. Or: Lock the RoutingConfig row (SELECT FOR UPDATE) before reading pointer.
3. In tests, simulate concurrent lead creation and verify each agent gets equal distribution (no agent skipped).

**Warning signs:** Over time, some agents receive many more leads than others, or same agent assigned consecutively when rotation should vary.

### Pitfall 4: Drag-Drop Optimistic Update Leaves Card in Wrong Column if Network Fails

**What goes wrong:** User drags card to new column, card moves instantly (optimistic). Network request fails silently (server down, timeout). Card stays in wrong column in browser, but DB still has old stage. When user refreshes, card pops back. Confusion.

**Why it happens:** Optimistic update assumes success; if no error callback fires, card stays in new position forever.

**How to avoid:**
1. Always await the POST request and show error toast if it fails.
2. Add a 5-second timeout to the request; if timeout, snap back and show "Server not responding."
3. Show a subtle "saving..." indicator during the request; turn it into a checkmark or X when done.

**Warning signs:** Support complaints about "my drag-drop didn't work but the card moved anyway."

### Pitfall 5: Time-Elapsed Rule Scan Misses Tenants if Iteration Fails Mid-Loop

**What goes wrong:** TimeElapsedRuleScanJob iterates 100 tenants, tenant #50 throws an exception (corrupted data, permission denied). Loop stops, tenants 51-100 never get their rules evaluated for that hour. Some time-based automations silently fail.

**Why it happens:** No try-catch per tenant — one bad tenant kills the whole scan.

**How to avoid:**
1. Wrap each tenant in try-catch in TimeElapsedRuleScanJob.ScanTimeElapsedRulesAsync.
2. Log the error but continue to next tenant.
3. Optional: emit a domain event so admin dashboards can flag tenants with evaluation errors.
4. In tests, create two tenants, corrupt one tenant's data, verify the other tenant's rules still fire.

**Warning signs:** Logs show single tenant error, then no more rule evaluations for any tenant in that hour.

## Code Examples

Verified patterns from official sources:

### Stateless State Machine Setup

```csharp
// Source: https://github.com/dotnet-state-machine/stateless
using Stateless;

public class StageTransitionConfiguration
{
    public static StateMachine<Guid, Guid> BuildStateMachine(
        Guid currentStageId,
        List<StageTransition> allowedTransitions)
    {
        var sm = new StateMachine<Guid, Guid>(currentStageId);

        foreach (var transition in allowedTransitions)
        {
            sm.Configure(transition.FromStageId)
                .Permit(transition.ToStageId, transition.ToStageId);
        }

        return sm;
    }

    public static bool IsTransitionValid(
        StateMachine<Guid, Guid> sm,
        Guid targetStageId)
    {
        return sm.CanFire(targetStageId);
    }
}
```

### RulesEngine Workflow Condition Evaluation

```csharp
// Source: https://microsoft.github.io/RulesEngine/
using RulesEngine.Models;

public class WorkflowConditionEvaluator
{
    private readonly RulesEngine.RulesEngine _engine;

    public async Task<bool> EvaluateConditionAsync(
        string conditionJson,
        Lead lead)
    {
        var ruleSet = JsonConvert.DeserializeObject<RuleSet>(conditionJson);
        var results = await _engine.ExecuteAllRulesAsync(ruleSet, lead);

        return results.All(r => r.IsSuccessful);
    }
}

// Example conditionJson:
// {
//   "name": "rule1",
//   "expression": "lead.DaysInStage > 7",
//   "actions": { "onSuccess": { "name": "log", "params": {} } }
// }
```

### Hangfire Recurring Job for Hourly Scan

```csharp
// Source: Hangfire documentation (https://www.hangfire.io/features.html)
// In ConfigureServices.cs:
services.AddHangfire(config =>
    config.UsePostgreSqlStorage(connectionString));

services.AddHangfireServer();

// In Program.cs or a background service:
RecurringJob.AddOrUpdate<TimeElapsedRuleScanJob>(
    "time-elapsed-rule-scan",
    job => job.ScanTimeElapsedRulesAsync(CancellationToken.None),
    Cron.Hourly  // Runs at minute 0 of every hour
);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hard-coded pipeline stages | Tenant-configurable stages with transitions | Phase 2 | Pipelines now differ per tenant; no assumption of "standard" workflow |
| Manual lead assignment only | Routing rules (round-robin + territory) | Phase 4 | Scales from 5 users to 50+ without admin overhead |
| No automation | Event-driven and time-based workflow rules | Phase 4 | Lead lifecycle can be partially automated; reduces manual toil |
| Synchronous rule evaluation | Async rule evaluation via Hangfire outbox pattern | Phase 4 | API responses fast; automation doesn't block user actions |

**Deprecated/outdated:**
- Windows Workflow Foundation (WF): Not supported in .NET 5+; Stateless + RulesEngine are the modern alternatives.
- Hard-coded routing logic: Phase 4 makes routing configurable so no code changes needed to adjust agent rotation.

## Open Questions

1. **Kanban virtual scroll: How many columns and how many leads per column will typical tenants have?**
   - What we know: D-03 specifies 20 leads per column, virtual scroll loads more. But if a tenant has 500 leads in one stage, 20 per scroll is clunky.
   - What's unclear: Pagination strategy (cursor-based, offset-limit, or infinite scroll?), when to update columns in real-time if another user drags a lead in?
   - Recommendation: Use cursor-based pagination (keyed by lead.CreatedAt) for stable order. Load next 20 on column scroll. On SignalR notification of a drag-drop, re-fetch the column if lead moved from outside the current view.

2. **WorkflowRule.ConditionJson: How expressive should expressions be?**
   - What we know: RulesEngine handles `lead.DaysInStage > 7` and boolean operators.
   - What's unclear: Can rules access custom field values? Can they aggregate (count leads in same stage, compare to threshold)?
   - Recommendation: Phase 4 supports field access and comparisons only. Aggregate functions (count, sum) deferred to Phase 5.

3. **Round-robin routing with different agent pools per dimension:**
   - What we know: D-14 says route by source OR custom field value. D-13 says strict round-robin.
   - What's unclear: If routing dimension is "industry" and a lead's industry is "Tech", do we only rotate among Tech-assigned agents, or all agents?
   - Recommendation: Rotate only among agents configured for that dimension value. Fall back to all agents if no dimension-specific agents exist.

4. **Audit trail for workflow rule execution:**
   - What we know: D-12 says rules can be toggled on/off, but no execution history in v1.
   - What's unclear: How will admins debug why a rule didn't fire? Where do we log "Rule X evaluated false for lead Y"?
   - Recommendation: Log to application logs (Serilog) with LogLevel.Information. Store one WorkflowRuleExecution record per rule per lead per day (deduplicated by ruleId+leadId+date). Defer full audit UI to Phase 5.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Testcontainers PostgreSQL 4.3.0 |
| Config file | (no config file; fixtures in IronMonkey.Tests/Fixtures/PostgreSqlFixture.cs) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Pipeline" -x` |
| Full suite command | `dotnet test IronMonkey.Tests -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PIPE-01 | Kanban board displays leads grouped by stage; drag-drop moves lead; invalid transition rejected with error | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~KanbanBoardTests.CanMoveLead" -x` | ❌ Wave 0 |
| PIPE-02 | Task created with due date, priority, assignee; appears in assignee's task list | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TaskTests.CanCreateTask" -x` | ❌ Wave 0 |
| PIPE-03 | NewLeadCreated event triggers routing logic; lead assigned to next agent in round-robin | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LeadRoutingTests.RoundRobinAssignment" -x` | ❌ Wave 0 |
| PIPE-04 | WorkflowRule with trigger/condition/action is created; condition evaluated for leads; matching action fired | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WorkflowRuleTests.CanEvaluateRule" -x` | ❌ Wave 0 |
| PIPE-05 | StateTransition table enforces allowed transitions; lead cannot move to disallowed stage; error returned | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~StateTransitionTests.InvalidTransitionRejected" -x` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Pipeline\|Task\|Routing\|WorkflowRule\|StateTransition" -x` (quick run, ~30 sec)
- **Per wave merge:** `dotnet test IronMonkey.Tests -x` (full suite, ~2 min)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `IronMonkey.Tests/Integration/KanbanBoardTests.cs` — covers PIPE-01 (move lead, invalid transition error)
- [ ] `IronMonkey.Tests/Integration/TaskTests.cs` — covers PIPE-02 (create task, list by assignee)
- [ ] `IronMonkey.Tests/Integration/LeadRoutingTests.cs` — covers PIPE-03 (round-robin assignment, routing config)
- [ ] `IronMonkey.Tests/Integration/WorkflowRuleTests.cs` — covers PIPE-04 (rule evaluation, trigger firing)
- [ ] `IronMonkey.Tests/Integration/StateTransitionTests.cs` — covers PIPE-05 (transition validation, error messages)
- [ ] `IronMonkey.Tests/Unit/StageTransitionConfigurationTests.cs` — Stateless state machine graph setup
- [ ] PostgreSqlFixture extension: Add helper method `CreateLeadWithStageAsync(stageId)` for test setup
- [ ] Framework install: `dotnet add IronMonkey.Tests package Stateless --version 5.20.1` and `RulesEngine --version 5.1.2`

## Sources

### Primary (HIGH confidence)

- **Stateless GitHub** (https://github.com/dotnet-state-machine/stateless) — State machine library patterns, fluent API, tested against .NET 10
- **RulesEngine GitHub** (https://microsoft.github.io/RulesEngine/) — JSON-based rule condition evaluation, Microsoft-backed, type-safe expression parsing
- **Hangfire Documentation** (https://www.hangfire.io) — Recurring jobs, background job patterns, PostgreSQL storage
- **Syncfusion Blazor Kanban** (https://blazor.syncfusion.com/documentation/kanban/drag-and-drop) — Drag-drop event handling, virtual scroll patterns
- **Microsoft .NET SignalR** (https://learn.microsoft.com/en-us/aspnet/core/signalr) — Real-time push notifications, built-in to .NET 10
- Project code: `IronMonkey.ApiService/BackgroundJobs/OutboxProcessingJob.cs`, `IronMonkey.Data/Entities/PipelineStage.cs`, `IronMonkey.Tests/Integration/PipelineStageTests.cs` — Existing patterns for tenant DB context, Hangfire jobs, integration tests

### Secondary (MEDIUM confidence)

- [GitHub - dotnet-state-machine/stateless](https://github.com/dotnet-state-machine/stateless) — Verified current version 5.20.1 supports .NET 10
- [NuGet Stateless](https://www.nuget.org/packages/stateless/) — Latest version info
- [RulesEngine npm/NuGet](https://microsoft.github.io/RulesEngine/) — Condition expression syntax documented

### Tertiary (LOW confidence, for validation)

- [WebSearch: .NET workflow engine options 2026] — Found Elsa Workflows, WorkflowEngine.IO; not chosen (heavier than needed for this phase)
- [WebSearch: Blazor drag-drop patterns] — General patterns, implementation details vary by component library

## Metadata

**Confidence breakdown:**

- **Standard stack:** HIGH — Stateless and RulesEngine are industry-standard, verified on NuGet with .NET 10 support. Hangfire and SignalR already in use in IronMonkey.
- **Architecture:** HIGH — Patterns based on existing codebase (outbox job pattern, endpoint structure, tenant DB context factory). State machine and workflow rule evaluation are well-established patterns.
- **Pitfalls:** HIGH — Based on lessons learned from Phase 1-3 implementation and common CRM automation pitfalls documented in workflow engine literature.
- **Validation Architecture:** HIGH — Test framework (xUnit + Testcontainers) already in place; mapping requirements to tests follows existing Phase 1-3 patterns.

**Research date:** 2026-03-22
**Valid until:** 2026-04-15 (stability: libraries are mature, no breaking changes expected; if new major .NET version released, refresh then)
