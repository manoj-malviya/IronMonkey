# Architecture Patterns — Multi-Tenant Configurable Lead Management System

**Domain:** Multi-tenant Lead Management SaaS with DB-per-tenant isolation, configurable schema, workflow engine
**Researched:** 2026-03-19
**Confidence:** HIGH (Microsoft Learn + community patterns verified)

## Executive Summary

A multi-tenant Lead Management system requires **three distinct architectural layers**: a shared **Orchestration Layer** (shared database, tenant resolution, provisioning), isolated **Tenant Data Layer** (per-tenant databases with configurable schema), and an **Event-Driven Communication Layer** (workflow dispatch, omnichannel messaging). The system must balance data isolation (database-per-tenant) with operational simplicity (shared migrations, templates) and tenant-specific flexibility (configurable fields, workflows, pipelines).

Your existing .NET Aspire + Blazor Server foundation maps well to this structure. The key additions are:
1. **Tenant provisioning service** — automated database creation and initialization
2. **Multi-database context factory** — scoped DbContext with dynamic connection routing
3. **Configurable entity model layer** — hybrid relational + EAV for flexible fields
4. **Workflow/trigger engine** — rules evaluator for state transitions and auto-actions
5. **Event relay service** — outbox-to-message-broker dispatch for omnichannel

---

## Recommended Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                           │
│  Blazor Server Web (IronMonkey.Web)                              │
│  ├─ Tenant UI (tenant-specific pages)                            │
│  ├─ Admin UI (multi-tenant admin, recipes)                       │
│  └─ HttpClient → API Service (authenticated, scoped to tenant)   │
└──────────────┬──────────────────────────────────────────────────┘
               │ HTTP requests with TenantId claim
┌──────────────▼──────────────────────────────────────────────────┐
│                    ORCHESTRATION LAYER                           │
│  Shared Database (SqlServer, Postgres, or managed cloud DB)      │
│  ├─ MASTER: Tenants, Subscriptions, Industry Recipes             │
│  ├─ USERS: Accounts, Identities, Roles, Permissions             │
│  ├─ TEMPLATES: Lead field definitions, statuses, pipelines       │
│  ├─ OUTBOX: Domain events (tenant lifecycle, user actions)       │
│  └─ CONFIG: System configuration, feature flags                  │
└──────────────┬──────────────────────────────────────────────────┘
               │ Tenant ID resolved from JWT claim
┌──────────────▼──────────────────────────────────────────────────┐
│               API SERVICE & TENANT ROUTING                       │
│  IronMonkey.ApiService (Minimal APIs)                            │
│  ├─ [*] Tenant Resolution Middleware                             │
│  │   └─ Extract TenantId from claims, resolve connection string  │
│  ├─ [*] Multi-DB DbContext Factory (Scoped)                      │
│  │   └─ Route to tenant-specific database                        │
│  ├─ [/auth] Authentication Endpoints                             │
│  ├─ [/onboarding] Tenant Setup (recipe selection, init)          │
│  ├─ [/leads] Lead CRUD, search, bulk operations                  │
│  ├─ [/config] Tenant Configuration (fields, statuses, pipelines) │
│  ├─ [/workflows] Workflow definition & execution                 │
│  ├─ [/communications] Message dispatch (email, SMS, WhatsApp)    │
│  └─ [/admin] Multi-tenant administration                         │
└──────────────┬──────────────────────────────────────────────────┘
               │
     ┌─────────┼─────────┬────────────────────┐
     │         │         │                    │
     ▼         ▼         ▼                    ▼
 [TENANT 1] [TENANT 2] [TENANT 3]  ... [TENANT N]
 Database    Database    Database        Database
 (Isolated)  (Isolated)  (Isolated)      (Isolated)

   ├─ LEADS (custom fields, configurable schema)
   ├─ CUSTOMERS
   ├─ LEAD_STATUSES (tenant-defined)
   ├─ PIPELINES (tenant-defined workflows)
   ├─ WORKFLOW_TRIGGERS
   ├─ USERS (tenant members)
   ├─ OUTBOX (tenant events)
   └─ CUSTOM_FIELDS (field definitions + values)

┌────────────────────────────────────────────────────────────────┐
│              WORKFLOW & EVENT-DRIVEN LAYER                      │
│  ├─ Workflow Engine (Hosted Service)                            │
│  │   ├─ Trigger Evaluator (listen to lead state changes)        │
│  │   ├─ Rule Engine (conditional logic: if/then/else)           │
│  │   └─ Action Executor (assign, notify, schedule)              │
│  │                                                              │
│  ├─ Event Relay (Hosted Service)                               │
│  │   ├─ Poll Outbox tables (all tenants)                        │
│  │   ├─ Classify: Domain events (system) vs Comms events        │
│  │   └─ Dispatch: Workflow engine, message broker               │
│  │                                                              │
│  └─ Communication Dispatcher                                    │
│      ├─ Enqueue messages to email service                       │
│      ├─ Enqueue messages to SMS provider (Twilio, etc.)        │
│      └─ Enqueue messages to WhatsApp Business API              │
└────────────────────────────────────────────────────────────────┘
```

---

## Component Boundaries

### 1. **Orchestration Layer** (Shared Database)

**Responsibility:** Tenant lifecycle, user authentication, system configuration, recipe templates.

| Component | Owns | Communicates With |
|-----------|------|-------------------|
| **Tenant Service** | Tenant creation, subscription management, provisioning trigger | DB-per-Tenant Factory, Provisioning Service |
| **Identity Service** | User accounts, JWT generation, password reset, multi-tenant auth | Tenant Service, API routing middleware |
| **Recipe Service** | Industry templates (automobile, real estate, insurance, etc.), template versioning | Tenant Service (on provisioning) |
| **Configuration Service** | Feature flags, system settings, billing config (future) | Any service needing flags |
| **Outbox Publisher** | Tenant lifecycle events (created, activated, suspended) | Event Relay Service |

**Data Model (Orchestration/Master Database):**
```
Tenants
├─ TenantId (PK)
├─ Name, Slug
├─ SubscriptionPlan (free, pro, enterprise)
├─ Status (active, suspended, deleted)
├─ DatabaseConnectionString (encrypted, per-tenant DB path)
├─ ProvisioningStatus (pending, completed, failed)
├─ ProvisionedAt, RecipeId (fk)

Users (shared across system, identity context)
├─ UserId (PK)
├─ IdentityId (ASP.NET Identity foreign key)
├─ Email, Name
├─ Role (super-admin, account-owner, admin, user)

Recipes
├─ RecipeId (PK)
├─ Industry (automobile, real_estate, insurance, services)
├─ FieldDefinitions (serialized JSON or EAV reference)
├─ DefaultStatuses, DefaultPipelines
├─ Version

OutboxMessages
├─ Id (PK)
├─ EventType (TenantCreated, TenantActivated, etc.)
├─ Payload (serialized event data)
├─ CreatedAt, ProcessedAt
├─ IsProcessed
```

---

### 2. **Tenant Data Layer** (Per-Tenant Isolated Databases)

**Responsibility:** Lead records, tenant-specific configuration, workflow definitions, custom fields.

**Each tenant database is completely isolated and contains:**

| Component | Owns | Communicates With |
|-----------|------|-------------------|
| **Lead Entity** | Lead records, audit trail, soft deletes | Lead Service, Workflow Engine |
| **Custom Field Store** | Field definitions (name, type, required) + field values | Configurable Entity Model Layer |
| **Status & Pipeline** | Lead statuses, pipeline stages (tenant-defined) | Workflow Engine, Lead Service |
| **Workflow Definitions** | Trigger rules, conditional logic, action mappings | Workflow Engine |
| **User & Permissions** | Tenant members, roles, field-level permissions | Identity context, API authorization |
| **Event Outbox** | Tenant-specific domain events (lead created, status changed) | Event Relay Service |

**Data Model (Per-Tenant Database):**
```
Leads
├─ LeadId (PK)
├─ TenantId (redundant, always same tenant)
├─ Name, Email, Phone
├─ CustomFieldValues (hybrid: standard columns + EAV table)
├─ CurrentStatus (FK to LeadStatuses)
├─ CurrentPipeline (FK to Pipelines)
├─ CreatedBy, CreatedAt, UpdatedAt
├─ IsDeleted (soft delete)

CustomFieldDefinitions
├─ FieldId (PK)
├─ FieldName, FieldType (text, number, date, dropdown, etc.)
├─ IsRequired, DisplayOrder
├─ DropdownOptions (if type=dropdown, JSON)

CustomFieldValues (EAV table for sparse fields)
├─ FieldValueId (PK)
├─ LeadId (FK)
├─ FieldId (FK)
├─ Value (stored as string, application converts type)

LeadStatuses
├─ StatusId (PK)
├─ Name (e.g., "Inquiry", "Qualified", "Negotiation")
├─ Color (for UI)
├─ IsTerminal (is this a final state?)
├─ Sequence (display order)

Pipelines
├─ PipelineId (PK)
├─ Name (e.g., "Sales Pipeline", "Support Pipeline")
├─ Description
├─ Stages (ordered list of LeadStatuses)

WorkflowDefinitions
├─ WorkflowId (PK)
├─ Name, Description
├─ TriggerType (on_lead_created, on_status_change, on_field_change, manual)
├─ TriggerCondition (JSON: "when status = Inquiry AND source = web_form")
├─ Actions (JSON array: assign, notify, schedule_task, etc.)
├─ IsActive, CreatedBy, CreatedAt

OutboxMessages (per-tenant)
├─ Id (PK)
├─ EventType (LeadCreated, StatusChanged, FieldUpdated, etc.)
├─ AggregateId (LeadId, UserId, etc.)
├─ Payload (serialized event data)
├─ CreatedAt, ProcessedAt
├─ IsProcessed
```

---

### 3. **Configurable Entity Model Layer** (API Service)

**Responsibility:** Map tenant-defined schema to runtime entity model, handle custom fields.

| Component | Pattern | Usage |
|-----------|---------|-------|
| **Configurable Lead Entity** | Hybrid: standard columns + custom field accessors | `lead.GetFieldValue<T>("custom_field_name")`, `lead.SetFieldValue("field", value)` |
| **Field Type Converter** | Type coercion (string → int, date, bool) | Custom field value storage/retrieval |
| **Validation Engine** | Dynamic validation rules per field | Fluent validation with field definitions |
| **Dynamic DbContext** | EF Core model building at runtime OR schema per tenant | Configure custom properties via `HasProperty()` in OnModelCreating |

**Implementation Strategy — Hybrid Relational + EAV:**

For IronMonkey, use a **hybrid approach**:
- **Standard columns** (Lead.Name, Email, Phone, Status, CreatedAt) — remain relational for performance and filtering
- **Custom fields** — stored in `CustomFieldValues` EAV table (like Magento, Salesforce)
- **Benefits:** Standard queries are fast, custom fields are flexible, tenants can add unlimited fields without schema changes

**Code Example (C#):**
```csharp
// Configuration in DbContext.OnModelCreating
modelBuilder.Entity<Lead>()
    .OwnsOne(l => l.CustomFieldValues, cf => {
        cf.HasKey(cv => cv.FieldValueId);
        cf.HasMany(cv => cv.Values)
            .WithOne()
            .HasForeignKey(v => v.FieldValueId);
    });

// Usage in application code
var lead = new Lead { Name = "Alice", Email = "alice@example.com" };
lead.SetCustomField("source", "website_form");
lead.SetCustomField("budget", "10000");
lead.SetCustomField("follow_up_date", DateTime.Now.AddDays(7));

var source = lead.GetCustomField<string>("source");
var budget = lead.GetCustomField<decimal?>("budget");
```

---

### 4. **Workflow & State Machine Layer**

**Responsibility:** Evaluate triggers, execute conditional logic, dispatch auto-actions.

| Component | Pattern | Triggers |
|-----------|---------|----------|
| **Workflow Trigger Evaluator** | Hosted Service, polls Outbox | Lead created, status changed, field updated, manual trigger |
| **Rule Engine** | Simple condition evaluator (if/then/else, AND/OR) | Evaluates trigger conditions against lead/custom fields |
| **Action Executor** | Dispatches actions to appropriate service | Assign lead, send notification, schedule follow-up, update field |
| **State Machine** | Validates lead status transitions | Ensures only valid status transitions (Inquiry → Qualified → Negotiation) |

**Workflow Definition JSON (stored in WorkflowDefinitions):**
```json
{
  "workflowId": "uuid",
  "name": "Auto-Assign Qualified Leads",
  "trigger": {
    "type": "on_status_change",
    "fromStatus": "Inquiry",
    "toStatus": "Qualified"
  },
  "conditions": {
    "operator": "AND",
    "rules": [
      { "field": "budget", "operator": ">=", "value": 5000 },
      { "field": "source", "operator": "==", "value": "web_form" }
    ]
  },
  "actions": [
    {
      "type": "assign",
      "assigneeId": "user-uuid",
      "reason": "auto_qualified"
    },
    {
      "type": "notify",
      "channel": "email",
      "recipient": "assignee",
      "template": "lead_assigned"
    },
    {
      "type": "schedule_task",
      "taskName": "first_contact",
      "daysDelay": 1
    }
  ]
}
```

**Trigger Evaluation Flow:**
1. Lead status changes (UpdateLeadStatus endpoint)
2. Event persisted in tenant's Outbox
3. Workflow Trigger Evaluator polls Outbox (background service)
4. For each unprocessed event, load WorkflowDefinitions where trigger matches
5. For each matching workflow, evaluate conditions against current lead state
6. If conditions pass, execute actions (assign, notify, schedule)
7. Mark outbox event as processed

---

### 5. **Event-Driven Communication Layer**

**Responsibility:** Dispatch tenant events to workflow engine and omnichannel communication services.

| Component | Pattern | Listens For |
|-----------|---------|------------|
| **Event Relay Service** | Hosted Service, scans all tenant Outbox tables | Any domain event in any tenant database |
| **Event Classification** | Route to workflow engine or communication dispatcher | EventType determines destination |
| **Workflow Trigger Dispatch** | Forward events to workflow evaluator | LeadCreated, StatusChanged, FieldUpdated, ManualTrigger |
| **Communication Dispatcher** | Enqueue messages to external providers | NotificationTriggered, MessageSent, TaskScheduled |
| **Message Queue Adapter** | Adapter pattern for email/SMS/WhatsApp providers | Abstracts Twilio, SendGrid, WhatsApp Business API |

**Event Flow (End-to-End):**
```
1. Lead Created
   ├─ LeadService.CreateLead() → raises LeadCreated domain event
   ├─ DbContext.SaveChangesAsync() → captures domain event → OutboxMessage
   │
2. Event Relay Polling (runs every 5 seconds across all tenant DBs)
   ├─ Scan Orchestration Outbox → classify as system or tenant event
   ├─ Scan Each Tenant Outbox → classify by EventType
   │
3. Route by Classification
   ├─ [Workflow Events] → Workflow Trigger Evaluator
   │   ├─ Evaluate WorkflowDefinitions
   │   ├─ Execute matching actions
   │   └─ Enqueue CommsMessage
   │
   ├─ [Comms Events] → Communication Dispatcher
   │   ├─ Resolve recipient address (email, phone, whatsapp)
   │   ├─ Enqueue to provider queue
   │   └─ Return delivery status
   │
   └─ [System Events] → Log / Audit
       └─ Update analytics
   │
4. Message Delivery (Async)
   ├─ EmailService polls queue, sends via SMTP
   ├─ SmsService polls queue, sends via Twilio
   └─ WhatsAppService polls queue, sends via WhatsApp API
   │
5. Delivery Feedback
   ├─ Update CommsMessage status (sent, failed, bounced)
   ├─ Raise DeliveryFailed event if needed
   └─ Mark Outbox as processed
```

**Omnichannel Architecture (Unified Dispatch):**
```
CommunicationDispatcher (unified interface)
├─ IMessageTemplate
│  ├─ Resolve template (stored in tenant DB)
│  ├─ Substitute variables ({{lead.name}}, {{tenant.name}})
│  └─ Return message body
│
├─ IRecipientResolver
│  ├─ Resolve email: lead.Email
│  ├─ Resolve phone: lead.Phone (format for SMS)
│  └─ Resolve whatsapp: lead.Phone (country code + Twilio token)
│
└─ IChannelAdapter
   ├─ EmailAdapter (SendGrid, SMTP)
   ├─ SmsAdapter (Twilio)
   └─ WhatsAppAdapter (Twilio Business API)
```

---

## Data Flow

### User Journey: Lead Capture Through Workflow Execution

#### Step 1: Tenant Onboarding (One-Time Setup)
```
User Flow:
1. Account Owner registers via /auth/signup
   ├─ CreateTenant in Orchestration DB
   ├─ Auto-create tenant database (provisioning service)
   ├─ Initialize schema (migrations)
   ├─ Create default roles/permissions
   └─ Load recipe (e.g., "Automobile Dealership")

2. Recipe Applied:
   ├─ Copy field definitions from recipe
   ├─ Create default statuses (Inquiry, Qualified, Negotiation, Won, Lost)
   ├─ Create default pipelines
   └─ Create sample workflows

3. Tenant configured and ready to use
   └─ Team members invited
```

#### Step 2: Lead Ingestion
```
User/System Flow:
1. Lead enters system (manual entry, form submission, or API)
   ├─ CreateLead endpoint validates lead against field definitions
   ├─ Save Lead entity with custom field values (EAV table)
   └─ Domain event raised: LeadCreated

2. SaveChangesAsync() in DbContext:
   ├─ Persist Lead and CustomFieldValues
   ├─ Capture LeadCreated event → OutboxMessage
   ├─ Commit transaction (atomic)
   └─ Return LeadId

3. Background: Event Relay polls tenant Outbox
   ├─ Find unprocessed LeadCreated event
   ├─ Route to Workflow Trigger Evaluator
   └─ Mark as processing
```

#### Step 3: Workflow Execution
```
Workflow Engine Flow:
1. Trigger Evaluator receives LeadCreated event
   ├─ Query WorkflowDefinitions for trigger_type = "on_lead_created"
   ├─ Load current lead state from database
   └─ For each matching workflow:

2. Condition Evaluation:
   ├─ Access lead.GetCustomField("source")
   ├─ Evaluate: IF (source == "web_form") AND (budget >= 5000)
   ├─ If conditions pass → execute actions
   └─ If conditions fail → skip workflow

3. Action Execution (if conditions met):
   ├─ Action: Assign
   │  └─ UpdateLead(assigneeId) → raise AssignmentChanged event
   │
   ├─ Action: Notify (email)
   │  └─ Create CommsMessage → enqueue to EmailService
   │
   ├─ Action: Schedule Task
   │  └─ Create FollowUpTask with dueDate
   │
   └─ Action: Update Field
       └─ SetCustomField("auto_assigned", true) → raise FieldUpdated event

4. Events raised from actions are persisted to Outbox
   └─ Mark original workflow trigger as processed
```

#### Step 4: Omnichannel Communication Dispatch
```
Communication Flow:
1. Event Relay sees CommsMessage in Outbox (type: "NotificationTriggered")
   ├─ Route to CommunicationDispatcher
   └─ Context: tenant, lead, channel (email/SMS/WhatsApp)

2. CommunicationDispatcher:
   ├─ Resolve recipient: lead.Email → "alice@example.com"
   ├─ Load template: "lead_assigned_email"
   ├─ Substitute variables: {lead.name} → "Alice", {assignee.name} → "Bob"
   ├─ Result: email body with personalized content
   └─ Enqueue to EmailAdapter

3. Async Email Service:
   ├─ Poll queue every 5 seconds
   ├─ Send via SMTP/SendGrid
   ├─ Capture delivery status (sent, bounced, failed)
   └─ Update CommsMessage status

4. Delivery Feedback:
   ├─ If sent: Mark message as delivered
   ├─ If bounced/failed: Raise DeliveryFailed event
   ├─ Create audit log for compliance
   └─ Mark Outbox as processed
```

---

## Suggested Build Order

The architecture layers have clear dependencies. Build in this sequence to minimize rework:

### Phase 1: Foundation (Weeks 1-2)
**Goal:** Core multi-tenant scaffolding + tenant provisioning.

| Component | Why First | Dependencies |
|-----------|-----------|--------------|
| Tenant Provisioning Service | Everything depends on this | DB creation, schema init |
| Multi-DB DbContext Factory | All data access depends on routing | Connection pool management |
| Tenant Resolution Middleware | Every request needs tenant context | JWT claim extraction |
| Base Entity Model (Orchestration DB) | Tenant/User records needed for auth | Identity service integration |

**Output:** Can create and provision tenants; requests are routed to correct database.

### Phase 2: Configurable Lead Model (Weeks 3-4)
**Goal:** Lead entity with custom fields; tenant configuration UI.

| Component | Why This Phase | Dependencies |
|-----------|----------------|--------------|
| Custom Field Definition Store | Fields needed for lead model | Orchestration DB |
| Hybrid Lead Entity (relational + EAV) | Lead ingestion and search | DbContext factory, field definitions |
| Field Validator (dynamic rules) | Validate on input | Field definitions |
| Tenant Config UI (Blazor pages) | Operators need to customize | Base Blazor app, API endpoints |
| Recipe Application | Defaults for new tenants | Custom field definitions |

**Output:** Tenants can define custom fields; leads can be created with custom data; operators can see/edit tenant config.

### Phase 3: Workflow Engine (Weeks 5-6)
**Goal:** Trigger evaluation, state machine, rule engine.

| Component | Why This Phase | Dependencies |
|-----------|----------------|--------------|
| Status/Pipeline Configuration | Workflows depend on pipeline | Custom fields (Phase 2) |
| Workflow Definition Store | Rule engine ingests workflows | Tenant configuration |
| Rule Engine (condition evaluator) | Core of workflow logic | Custom fields, statuses |
| State Machine Validator | Validate status transitions | Statuses/pipelines |
| Trigger Evaluator (hosted service) | Watches outbox and fires | Event outbox in place |

**Output:** Workflows can be created; leads transitioning status automatically trigger configured actions.

### Phase 4: Event-Driven & Communication (Weeks 7-8)
**Goal:** Event relay, omnichannel dispatch, external integrations.

| Component | Why This Phase | Dependencies |
|-----------|----------------|--------------|
| Event Relay Service (hosted) | Polls outbox and routes events | Outbox pattern (Phase 1), Trigger Evaluator (Phase 3) |
| Communication Dispatcher | Routes messages to channels | Lead, recipients, templates |
| Email Service + Template Store | Send transactional emails | Dispatcher, tenant config |
| SMS Service (Twilio adapter) | Send SMS messages | Dispatcher, phone number resolution |
| WhatsApp Service (Twilio adapter) | Send WhatsApp messages | Dispatcher, WhatsApp token mgmt |

**Output:** Workflows trigger notifications via email, SMS, WhatsApp; messages are logged and tracked.

### Phase 5: UI & Onboarding (Weeks 9-10)
**Goal:** Tenant self-service setup, lead dashboard, workflow builder.

| Component | Why This Phase | Dependencies |
|-----------|----------------|--------------|
| Onboarding UI (recipe selection) | Tenant setup workflow | Provisioning service, recipe templates |
| Lead Dashboard (Blazor pages) | View/manage leads | Lead data, custom fields (Phase 2) |
| Workflow Builder UI | Non-code workflow creation | Workflow definitions (Phase 3) |
| Tenant Admin UI | Configuration management | All config components |
| Communication History | Track delivery | Event tracking (Phase 4) |

**Output:** Operators can sign up, pick industry recipe, see leads, build workflows without code.

### Phase 6: Production Hardening (Weeks 11+)
**Goal:** Scale, security, compliance, analytics.

| Component | Why This Phase | Dependencies |
|-----------|----------------|--------------|
| Connection Pooling Optimization | Performance under load | DbContext factory (Phase 1) |
| Database Migration Automation | Manage schema updates across tenants | Migration strategy |
| Tenant Isolation Verification | Security audit | All phases |
| Audit Logging | Compliance/debugging | Event outbox (all phases) |
| Analytics/Dashboards | Business insights | All data collection complete |

---

## Key Architectural Patterns

### Pattern 1: Scoped DbContext Factory with Tenant Routing

**What:** DbContext lifetime is **Scoped** (not Singleton), resolved per HTTP request based on tenant context.

**When:** Every request that accesses tenant data.

**Why:** Tenant ID changes per user/session; connection string is looked up dynamically; if Singleton, all users share one connection string (breaks multi-tenancy).

**Example:**
```csharp
// In Program.cs
services.AddDbContextFactory<TenantDbContext>(options => {
    // Connection string is NOT set here
}, ServiceLifetime.Scoped);

// Register ITenantService as Scoped (extracts tenant from JWT)
services.AddScoped<ITenantService>();

// In TenantDbContext.OnConfiguring
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    var tenant = _tenantService.Tenant;
    var connStr = _configuration.GetConnectionString(tenant);
    options.UseSqlServer(connStr);
}
```

### Pattern 2: Outbox Pattern for Domain Events

**What:** Domain events are stored in the database (Outbox table) as part of the same transaction as business data, then published asynchronously.

**When:** Any operation that needs to trigger downstream actions (lead created, status changed).

**Why:** Guarantees "exactly once" delivery; avoids dual-write problem; if database commit fails, no event is published.

**Example:**
```csharp
// In Domain Entity (Lead)
public void RaiseDomainEvent(IDomainEvent @event)
{
    _domainEvents.Add(@event);
}

// In DbContext.SaveChangesAsync override
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
{
    var domainEvents = ChangeTracker.Entries<Entity>()
        .SelectMany(e => e.Entity.DomainEvents)
        .ToList();

    var outboxMessages = domainEvents.Select(e => new OutboxMessage {
        EventType = e.GetType().Name,
        Payload = JsonSerializer.Serialize(e),
        CreatedAt = DateTime.UtcNow
    }).ToList();

    await Set<OutboxMessage>().AddRangeAsync(outboxMessages);

    return await base.SaveChangesAsync(cancellationToken);
}
```

### Pattern 3: Hybrid Relational + EAV for Custom Fields

**What:** Standard columns (Name, Email, Phone) remain relational; custom tenant-defined fields stored in EAV (Entity-Attribute-Value) table.

**When:** Tenant-specific fields that don't fit the schema.

**Why:** Standard queries remain fast; custom fields are unlimited; no schema changes needed per tenant; Salesforce and Magento use this.

**Example:**
```csharp
// Lead entity
public class Lead : Entity
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }

    // Custom fields stored in EAV table
    public ICollection<CustomFieldValue> FieldValues { get; set; }
}

// Usage
var lead = new Lead { Name = "Alice", Email = "alice@example.com" };
lead.FieldValues.Add(new CustomFieldValue {
    FieldDefinitionId = budgetFieldId,
    Value = "50000" // Stored as string, converted to decimal in code
});
```

### Pattern 4: Rule Engine for Workflow Triggers

**What:** Workflows are stored as JSON (or database records); trigger conditions are evaluated at runtime against current lead state.

**When:** Leads change status, fields are updated, or manual triggers fire.

**Why:** Tenants can create workflows without code; conditions can reference custom fields; no redeployment needed.

**Example Evaluator:**
```csharp
public class RuleEngine
{
    public bool EvaluateConditions(Lead lead, WorkflowConditions conditions)
    {
        if (conditions.Operator == "AND")
            return conditions.Rules.All(rule => EvaluateRule(lead, rule));
        else if (conditions.Operator == "OR")
            return conditions.Rules.Any(rule => EvaluateRule(lead, rule));

        return false;
    }

    private bool EvaluateRule(Lead lead, Rule rule)
    {
        var fieldValue = lead.GetCustomField<dynamic>(rule.Field);
        return rule.Operator switch {
            ">=" => fieldValue >= rule.Value,
            "==" => fieldValue == rule.Value,
            "in" => rule.Value.Contains(fieldValue),
            _ => throw new NotImplementedException()
        };
    }
}
```

### Pattern 5: Message Adapter Pattern for Omnichannel

**What:** Abstract communication channels (email, SMS, WhatsApp) behind a common `IMessageAdapter` interface.

**When:** Sending notifications through any channel.

**Why:** Easy to swap providers (SendGrid → AWS SES) without changing business logic; supports multiple channels simultaneously.

**Example:**
```csharp
public interface IMessageAdapter
{
    Task<DeliveryResult> SendAsync(Message message, CancellationToken cancellationToken);
}

public class EmailAdapter : IMessageAdapter
{
    public async Task<DeliveryResult> SendAsync(Message message, CancellationToken ct)
    {
        using var client = new SmtpClient();
        await client.SendMailAsync(message.To, message.Subject, message.Body);
        return DeliveryResult.Success();
    }
}

public class SmsAdapter : IMessageAdapter
{
    public async Task<DeliveryResult> SendAsync(Message message, CancellationToken ct)
    {
        var result = await _twilio.Messages.CreateAsync(
            from: _twilioNumber,
            to: message.To,
            body: message.Body
        );
        return result.Status == "sent" ? DeliveryResult.Success() : DeliveryResult.Failed(result.ErrorMessage);
    }
}

// Usage
public class CommunicationDispatcher
{
    public async Task DispatchAsync(CommsMessage msg)
    {
        var adapter = _adapterFactory.GetAdapter(msg.Channel);
        return await adapter.SendAsync(msg, cancellationToken);
    }
}
```

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Singleton DbContextFactory with Multi-Tenant Assumptions
**What:** Register DbContextFactory as Singleton when using database-per-tenant.
**Why Bad:** Factory caches options at Singleton lifetime; connection string is only evaluated once; all users share the same tenant's connection.
**Instead:** Use **Scoped** factory lifetime; connection string is resolved per request from ITenantService.

### Anti-Pattern 2: Schema-Per-Tenant in Shared Database
**What:** Each tenant has a schema (tenant1.leads, tenant2.leads) in same database.
**Why Bad:** EF Core doesn't natively support schema-per-tenant; you'd need to override `ToTable(name, schema)` in OnModelCreating per request; migrations become complex; cross-tenant queries are error-prone.
**Instead:** Use **database-per-tenant** for true isolation OR **discriminator column** (TenantId filter) for cost-efficiency.

### Anti-Pattern 3: Storing All Custom Fields in JSON Blobs
**What:** Store all tenant fields as { "field1": "value1", ... } in a single JSON column.
**Why Bad:** Can't filter by custom fields efficiently; queries become slow (JSON parsing on every query); no type safety; hard to migrate custom fields.
**Instead:** Use **hybrid relational + EAV**: standard columns relational, sparse custom fields in EAV table.

### Anti-Pattern 4: Synchronous Workflow Execution in Request Handler
**What:** Handle lead status change → evaluate workflows → send emails → return response (all in one request).
**Why Bad:** Request timeouts if workflows are slow; external service failures (email provider down) fail the whole request; no retry mechanism.
**Instead:** Use **event-driven async pattern**: persist event to Outbox → return immediately → background service evaluates workflows asynchronously.

### Anti-Pattern 5: Direct Provider Calls Scattered Throughout Codebase
**What:** Call Twilio API, SendGrid API directly from multiple endpoints.
**Why Bad:** Coupling to external APIs; hard to test; hard to swap providers; retry logic duplicated.
**Instead:** Use **message adapter pattern** with a centralized CommunicationDispatcher; all external calls go through the dispatcher.

### Anti-Pattern 6: No Idempotency for Message Delivery
**What:** Process Outbox events without tracking delivery state; reprocess same event multiple times.
**Why Bad:** Duplicate emails/SMS to leads; workflow actions execute multiple times; data corruption.
**Instead:** Use **Inbox pattern**: track processed event IDs; skip if already processed; idempotent operations.

---

## Scalability Considerations

| Concern | At 100 Tenants | At 1K Tenants | At 10K+ Tenants |
|---------|----------------|---------------|-----------------|
| **Tenant Database Provisioning** | Manual creation acceptable | Semi-automated (script) | Fully automated with health checks |
| **Connection Pool Management** | One pool per tenant, simple routing | Multiple pools, consider DbContext pooling | Connection multiplexing, read replicas |
| **Outbox Polling** | Poll all outbox tables sequentially | Partition outbox by tenant hash | Distributed event bus (MassTransit, NServiceBus) |
| **Workflow Trigger Evaluation** | In-process hosted service | Single service, acceptable latency | Partitioned by tenant, multiple instances |
| **Message Delivery** | In-process email service | Separate queue, retry logic | Async message broker (RabbitMQ, Service Bus) |
| **Custom Field Queries** | EAV table fine for <10M rows | Index custom field tables by (LeadId, FieldId) | Denormalize frequently-queried custom fields |

---

## Component Communication Diagram

```
                    ┌──────────────────────┐
                    │   Blazor Server      │
                    │   Frontend (Web)     │
                    └──────────┬───────────┘
                               │ HTTP
                               ▼
            ┌──────────────────────────────────────┐
            │   ASP.NET Core Minimal APIs          │
            │  ┌──────────────────────────────────┐│
            │  │ Tenant Resolution Middleware      ││
            │  │ (Extract TenantId, resolve DB)    ││
            │  └──────────────────────────────────┘│
            │  ┌────────┬──────────┬──────────────┐ │
            │  │ /leads │ /config  │ /workflows   │ │
            │  └────┬───┴────┬─────┴──────┬───────┘ │
            └───────┼────────┼────────────┼─────────┘
                    │        │            │
        ┌───────────▼───┬────▼──────┬─────▼──────┐
        │               │            │            │
        ▼               ▼            ▼            ▼
   [Orchestration]  [Tenant DB 1] [Tenant DB 2] [Tenant DB N]
    Master DB        (Isolated)     (Isolated)    (Isolated)
    ├─ Tenants
    ├─ Users       ├─ Leads      ├─ Leads       ├─ Leads
    ├─ Recipes     ├─ CustomFields ├─ CustomFields ├─ CustomFields
    ├─ Outbox      ├─ Workflows  ├─ Workflows   ├─ Workflows
                   ├─ Outbox     ├─ Outbox      ├─ Outbox
                   └─ Statuses   └─ Statuses    └─ Statuses
                        ▲              ▲              ▲
                        │              │              │
        ┌───────────────┼──────────────┼──────────────┤
        │               │              │              │
        ▼               ▼              ▼              ▼
    ┌─────────────────────────────────────────────────────────┐
    │         Background Hosted Services                       │
    │  ┌─────────────────┐    ┌──────────────────────────────┐│
    │  │ Event Relay     │    │ Workflow Trigger Evaluator   ││
    │  │ (polls outbox)  │───▶│ (evaluates triggers & rules) ││
    │  └─────────────────┘    └────────┬─────────────────────┘│
    │                                   │                      │
    │  ┌──────────────────────────────┐ │                     │
    │  │ Communication Dispatcher     │◀┘                     │
    │  │ ├─ Email Service             │                       │
    │  │ ├─ SMS Service (Twilio)      │                       │
    │  │ └─ WhatsApp Service (Twilio) │                       │
    │  └──────────────────────────────┘                       │
    └─────────────────────────────────────────────────────────┘
            │                    │                  │
            ▼                    ▼                  ▼
        [SendGrid/    [Twilio SMS]    [Twilio WhatsApp]
         SMTP]
```

---

## Technology Stack Implications

| Layer | Technology | Why | Constraint |
|-------|----------|-----|-----------|
| **Presentation** | Blazor Server | Matches .NET Aspire foundation; real-time SignalR support | Must use scoped DbContextFactory (not Singleton) |
| **API** | Minimal APIs + ASP.NET Core | Lightweight, fast; already in place | Tenant resolution must happen in middleware |
| **Database** | SqlServer/PostgreSQL (one per tenant) | Production-grade; supports migrations; Azure/AWS managed options | Multiple connection strings in config; automated provisioning required |
| **ORM** | Entity Framework Core | Native .NET; supports multi-tenancy patterns; Blazor-friendly | Use Scoped lifetime; DbContext pooling for high load |
| **Events** | Outbox Pattern (in-process) | Simple, reliable, no external dependencies for MVP | For high-scale, graduate to MassTransit/NServiceBus |
| **Rules Engine** | Custom evaluator | Simple for MVP; JSON-based workflows | Can evolve to Drools.NET or Roslyn-based if complexity grows |
| **Message Queue** | In-process for MVP (Entity Framework) | Fast iteration; no infrastructure setup | For production 10K+ tenants, add RabbitMQ/Service Bus |
| **Communications** | SendGrid (email), Twilio (SMS/WhatsApp) | Industry standard; easy integration; good SDKs | Requires tenant API key management (secure storage) |

---

## Build Order Dependencies

```
Phase 1: Provisioning & Routing
├─ Tenant Provisioning Service
├─ Multi-DB DbContext Factory
├─ Tenant Resolution Middleware
└─ Event Outbox Setup
    │
    └─ Phase 2: Configurable Leads
       ├─ Custom Field Definitions
       ├─ Hybrid Lead Entity (relational + EAV)
       ├─ Field Validators
       └─ Recipe Templates
           │
           └─ Phase 3: Workflows
              ├─ Status/Pipeline Configuration
              ├─ Rule Engine
              ├─ Trigger Evaluator (Hosted Service)
              └─ State Machine Validator
                  │
                  └─ Phase 4: Omnichannel
                     ├─ Event Relay Service
                     ├─ Communication Dispatcher
                     ├─ Email/SMS/WhatsApp Adapters
                     └─ Message Templates
                         │
                         └─ Phase 5: UI & Onboarding
                            ├─ Tenant Onboarding Flow
                            ├─ Lead Management Dashboard
                            ├─ Workflow Builder UI
                            └─ Tenant Admin Console
```

---

## Component Responsibility Matrix

| Component | Responsibility | Communicates With | Lifetime |
|-----------|-----------------|-------------------|----------|
| Tenant Provisioning Service | Create tenant DB, init schema | Master DB, Migration runner | Scoped/Transient (triggered) |
| ITenantService | Resolve current tenant from JWT | Middleware, DbContext | Scoped |
| DbContext (per tenant) | Persist lead, field, workflow data | EF Core | Scoped |
| RuleEngine | Evaluate workflow conditions | WorkflowDefinition, Lead | Transient |
| WorkflowTriggerEvaluator | Poll outbox, execute workflows | All tenant DBs, RuleEngine, CommunicationDispatcher | Hosted Service (Singleton) |
| CommunicationDispatcher | Route messages to channels | Email/SMS/WhatsApp adapters | Transient (per message) |
| IMessageAdapter | Send message via channel | External provider API | Transient (per message) |
| EventRelayService | Poll all outbox tables, classify events | All tenant DBs, MessageBroker | Hosted Service (Singleton) |
| FieldValidator | Validate custom field values | Field definition rules | Transient (per request) |
| RecipeService | Load industry templates | Master DB | Scoped |

---

## Configuration Per Phase

**Phase 1 (Provisioning):** Database connection strings, tenant migration folder location, admin credentials for master DB.

**Phase 2 (Configurable Leads):** Supported field types (text, number, date, dropdown), max custom fields per tenant.

**Phase 3 (Workflows):** Max workflows per tenant, max trigger conditions per rule, action timeout duration.

**Phase 4 (Communications):** SendGrid/SMTP API keys (tenant-specific or shared?), Twilio account SID/token, WhatsApp Business phone number.

**Phase 5 (UI):** Tenant branding (logo, colors), onboarding experience (required steps), feature flags per subscription tier.

---

## Sources

- [Multi-tenancy - EF Core | Microsoft Learn](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [Multi-Tenant Architecture - SaaS App Design Best Practices](https://relevant.software/blog/multi-tenant-architecture/)
- [Multi-Tenant Database Architecture Patterns Explained](https://www.bytebase.com/blog/multi-tenant-database-architecture-patterns-explained/)
- [How to Build the Outbox Pattern in .NET](https://oneuptime.com/blog/post/2026-01-26-dotnet-outbox-pattern/view)
- [Workflow Engine vs. State Machine](https://workflowengine.io/blog/workflow-engine-vs-state-machine/)
- [Omnichannel Communication: Guide for 2026](https://chatarmin.com/en/blog/omnichannel-communication)
- [Entity–attribute–value model - Wikipedia](https://en.wikipedia.org/wiki/Entity%E2%80%93attribute%E2%80%93value_model)
- [SaaS Onboarding Flows That Actually Convert in 2026](https://www.saasui.design/blog/saas-onboarding-flows-that-actually-convert-2026)
- [Lead generation automation workflows: delivering results in 2026](https://monday.com/blog/crm-and-sales/lead-generation-automation/)
