# Research Summary: IronMonkey Architecture for Multi-Tenant Lead Management

**Domain:** Multi-tenant SaaS Lead Management System (DB-per-tenant, configurable schema, workflow engine)
**Researched:** 2026-03-19
**Overall Confidence:** HIGH

---

## Executive Summary

Your .NET Aspire + Blazor Server foundation is well-suited for multi-tenant Lead Management, but requires careful architectural decisions around three distinct layers: **Orchestration** (shared, tenant lifecycle), **Tenant Data** (isolated, configurable), and **Event-Driven Communication** (async, reliable).

The research reveals a converged industry pattern: **database-per-tenant for isolation** + **Scoped DbContext factories** + **EAV or JSONB for custom fields** + **Outbox pattern for events** + **background service for workflow execution** + **message adapters for omnichannel**. This is proven by Salesforce (custom fields), Magento (EAV), and modern CRM/SaaS platforms.

### Critical Architectural Insights

1. **DbContext must be Scoped, not Singleton** — This is the #1 mistake. With database-per-tenant, connection string resolution happens per request; Singleton factories cache options and all users share one tenant's connection.

2. **Hybrid relational + EAV beats pure JSON** — Don't store all custom fields as JSON blobs. Standard columns (Name, Email, Phone, Status) remain relational for fast queries; sparse custom fields go in EAV table. This is how Salesforce and Magento handle configurability at scale.

3. **Workflows must be event-driven async** — Don't execute workflows synchronously in request handlers (lead created → evaluate rules → send email → return response). This causes request timeouts and cascading failures. Use Outbox pattern: persist event → return → background service evaluates asynchronously.

4. **Tenant provisioning must be automated** — Manual database creation doesn't scale to 1K tenants. Phase 1 must include automated provisioning: detect new tenant → create database → run migrations → initialize with recipe → mark ready.

---

## Key Findings

### The Three-Layer Architecture

| Layer | Purpose | Manages | Isolation |
|-------|---------|---------|-----------|
| **Orchestration** | Tenant lifecycle, identity, templates, billing | Master database (shared) | Logical (TenantId) |
| **Tenant Data** | Leads, workflows, custom fields | Per-tenant databases | Physical (separate DB) |
| **Event-Driven Comms** | Workflow triggers, message dispatch | Background hosted services | Logical (tenant context) |

### Database Strategy: Per-Tenant with Dynamic Routing

**Orchestration DB (Shared):**
- Tenants, Users (identity context), Recipes, Configuration, Outbox (tenant lifecycle events)

**Per-Tenant DB (Isolated):**
- Leads, CustomFields, Workflows, Statuses, Pipelines, Users (tenant members), Outbox (domain events)

**Connection Routing (Request-Time):**
```
1. JWT claim → ExtractTenantId
2. ITenantService.GetConnectionString(tenantId) → lookup in config/master DB
3. DbContext.OnConfiguring → UseSqlServer(connectionString)
4. DbContext lifetime = Scoped (not Singleton)
```

### Custom Field Storage: Hybrid Relational + EAV

**Standard Columns (Relational):**
- Lead.Name, Email, Phone, Status, Pipeline, CreatedAt, UpdatedAt
- Why: These are common across all tenants; queries need to filter on them; must be fast

**Custom Tenant Fields (EAV):**
- CustomFieldDefinitions: (FieldId, FieldName, FieldType, IsRequired)
- CustomFieldValues: (FieldValueId, LeadId, FieldId, Value)
- Why: Unlimited custom fields; no schema changes per tenant; sparse data (not every lead has every field)

**Benefit over JSON:**
- JSON blobs: Can't query "leads with Budget > 50000" efficiently; must parse JSON on every query
- EAV: Standard SQL queries with indexed fields; field type validation in application code; Salesforce uses this

### Workflow & State Machine Pattern

**Workflow Execution Flow:**
```
Lead Created
  ↓
  RaiseDomainEvent(LeadCreated)
  ↓
  DbContext.SaveChangesAsync() → Captures event → OutboxMessage
  ↓
  Event Relay polls Outbox (background service)
  ↓
  Route to Workflow Trigger Evaluator
  ↓
  Load WorkflowDefinitions (JSON: trigger conditions + actions)
  ↓
  Condition Evaluator checks: IF (budget >= 50000) AND (source == "web")
  ↓
  Execute Actions: assign, notify, schedule, update field
  ↓
  Persist action results to database
  ↓
  Trigger new events (AssignmentChanged, NotificationScheduled)
```

**Why Async (Not Sync):**
- Sync in request: If email service is slow/down, request times out
- Async via Outbox: Persist event atomically → return immediately → background service retries
- Guaranteed delivery: Event is in database; background service will process it eventually

### Omnichannel Communication Pattern

**Message Adapter Pattern** (abstract external APIs):
```
CommunicationDispatcher
  ├─ Load template: "lead_assigned_notification"
  ├─ Resolve recipient: lead.Email → "alice@example.com"
  ├─ Substitute variables: {lead.name} → "Alice"
  ├─ Select channel: email / SMS / WhatsApp
  ├─ Route to adapter: EmailAdapter / SmsAdapter / WhatsAppAdapter
  └─ Return delivery status

EmailAdapter → SendGrid or SMTP (swappable)
SmsAdapter → Twilio SMS
WhatsAppAdapter → Twilio WhatsApp Business API
```

**Why Adapters:**
- Easy to swap providers (SendGrid → AWS SES) without changing business logic
- Easy to add new channels (Teams, Slack)
- Centralized dispatch for auditing and retry logic

---

## Implications for Roadmap

### Phase Structure Recommended

| Phase | Goal | Delivers | Dependencies |
|-------|------|----------|--------------|
| **Phase 1** | Provisioning & Multi-DB Routing | Can create tenant and route requests to correct DB | None |
| **Phase 2** | Configurable Leads | Can define custom fields; leads accept custom data | Phase 1 |
| **Phase 3** | Workflow Engine | Workflows trigger on lead changes; conditions evaluated | Phase 1, 2 |
| **Phase 4** | Omnichannel Comms | Workflows send email/SMS/WhatsApp | Phase 1, 3 |
| **Phase 5** | UI & Onboarding | Tenants self-service sign up, build workflows | Phase 1-4 |
| **Phase 6** | Production Scale | 1K-10K tenants, connection pooling, migration automation | All phases |

### Critical Decisions for Each Phase

**Phase 1: Provisioning & Routing**
- [ ] Choose database platform (Azure SQL? AWS RDS? PostgreSQL on EC2?)
- [ ] Design tenant provisioning workflow (how do we create a new database?)
- [ ] Implement ITenantService + TenantResolutionMiddleware
- [ ] Make DbContextFactory Scoped (not Singleton)
- [ ] Wire outbox pattern into SaveChangesAsync

**Phase 2: Configurable Leads**
- [ ] Finalize custom field types (text, number, date, dropdown, currency, etc.)
- [ ] Design EAV table schema and indexes
- [ ] Build field validator that reads from CustomFieldDefinitions
- [ ] Create recipe templates (automobile, real estate, insurance, etc.)

**Phase 3: Workflow Engine**
- [ ] Define WorkflowDefinition JSON schema (triggers, conditions, actions)
- [ ] Build rule engine: simple evaluator for AND/OR/comparison
- [ ] Create WorkflowTriggerEvaluator hosted service
- [ ] Implement action executor (assign, notify, schedule)

**Phase 4: Omnichannel Comms**
- [ ] Set up SendGrid (or SMTP) for email
- [ ] Set up Twilio for SMS and WhatsApp
- [ ] Build message adapter pattern
- [ ] Create CommunicationDispatcher
- [ ] Design message templates

**Phase 5: UI & Onboarding**
- [ ] Onboarding survey (which industry, company size?)
- [ ] Lead dashboard (list, search, bulk actions)
- [ ] Workflow builder UI (no-code rule creation)
- [ ] Tenant admin console (config, users, integrations)

**Phase 6: Production Scale**
- [ ] Connection pool tuning for 1K+ tenants
- [ ] EAV query performance optimization
- [ ] Schema migration automation across tenant databases
- [ ] Audit logging for compliance
- [ ] Billing integration

### Build Order Rationale

**Why Phase 1 First:** Everything depends on provisioning and tenant routing. You can't test anything else without these.

**Why Phase 2 Before Phase 3:** Workflows need custom fields to exist before they can reference them in conditions.

**Why Phase 3 Before Phase 4:** Workflows create the events that trigger communications. Comms without workflows are just mail service; workflows without comms are silent automation.

**Why Phase 5 Last:** UI should reflect finished architecture; no point building UI for half-implemented features.

---

## Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| **Database-per-Tenant Isolation** | HIGH | Microsoft Learn + industry standard (Salesforce, HubSpot use this) |
| **Scoped DbContext + Tenant Routing** | HIGH | Explicit guidance from Microsoft; clear EF Core patterns |
| **Hybrid Relational + EAV** | HIGH | Proven at scale (Salesforce, Magento); clear trade-offs documented |
| **Outbox Pattern & Event-Driven** | HIGH | Microservices.io, Azure guidance, community consensus |
| **Workflow Rule Engine** | HIGH | Trigger-based automation is 2026 CRM standard; simple evaluators are well-understood |
| **Omnichannel (Email/SMS/WhatsApp)** | HIGH | Twilio is industry standard; APIs stable; SDKs good |
| **Provisioning at Scale** | MEDIUM | Architecture sound; implementation depends on chosen database platform and automation tools |
| **SignalR Scaling (Blazor Server)** | MEDIUM | Fine up to 1K concurrent connections; beyond that, need Azure SignalR Service or alternatives |

---

## Gaps to Address During Implementation

### Phase 1 Planning
- [ ] Database platform selection (SQL Server, PostgreSQL, or cloud-managed?)
- [ ] Provisioning automation strategy (script, PowerShell, API-driven?)
- [ ] Tenant isolation security model (encryption at rest, network isolation?)
- [ ] Migration rollout strategy (how to update schema for 1K+ tenants?)

### Phase 2 Planning
- [ ] EAV table indexing strategy for large datasets
- [ ] Custom field type support (which types are MVP? which are future?)
- [ ] Field dependency rules (can field visibility depend on another field's value?)
- [ ] Field validation rules (required, unique, min/max, regex?)

### Phase 3 Planning
- [ ] Workflow trigger types (on_created, on_status_change, on_field_change, scheduled, manual?)
- [ ] Action library design (what actions can workflows execute?)
- [ ] Rule engine extensibility (how to add new operators/conditions?)
- [ ] Error handling (what happens if a workflow action fails?)

### Phase 4 Planning
- [ ] Tenant API key management (where do SendGrid/Twilio credentials live?)
- [ ] Message template system (variable substitution, preview, versioning?)
- [ ] Delivery tracking (bounce handling, retry policy, audit trail?)
- [ ] In-process vs. message broker (when do we switch from in-process to RabbitMQ/Service Bus?)

### Phase 5 Planning
- [ ] Onboarding flow design (which screens are required vs. optional?)
- [ ] Recipe scope per industry (which fields/statuses per industry template?)
- [ ] Tenant branding (logo only, or colors/fonts/domain?)
- [ ] Role model and permissions (super-admin, account-owner, admin, user, or custom roles?)

---

## Recommended Next Steps

1. **Before Phase 1 starts:**
   - Lock in database platform choice (affects provisioning automation)
   - Prototype DbContextFactory with Scoped lifetime and dynamic connection routing
   - Test tenant context resolution via JWT claims

2. **During Phase 1:**
   - Build automated tenant provisioning (database creation, schema init)
   - Implement Outbox pattern in SaveChangesAsync
   - Create ITenantService and TenantResolutionMiddleware
   - Prove that requests route to correct tenant database

3. **Before Phase 2 starts:**
   - Design CustomFieldDefinition model (types, validation rules, constraints)
   - Design EAV table schema and create indexes
   - Prototype custom field accessor methods (lead.GetField<T>, lead.SetField)

4. **Before Phase 3 starts:**
   - Design WorkflowDefinition JSON schema
   - Prototype RuleEvaluator (AND/OR/comparison operators)
   - Prototype WorkflowTriggerEvaluator as hosted service

5. **Before Phase 4 starts:**
   - Integrate SendGrid/SMTP for email
   - Integrate Twilio for SMS and WhatsApp
   - Design message template system and adapter pattern

6. **Before Phase 5 starts:**
   - Design onboarding survey questions
   - Design lead dashboard UI (what columns? filters? bulk actions?)
   - Prototype workflow builder UI mockups

---

## Sources

**Multi-Tenancy & Isolation:**
- [Multi-tenancy - EF Core | Microsoft Learn](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [Multi-Tenant Architecture - SaaS App Design Best Practices](https://relevant.software/blog/multi-tenant-architecture/)
- [Multi-Tenant Database Architecture Patterns Explained](https://www.bytebase.com/blog/multi-tenant-database-architecture-patterns-explained/)

**Event-Driven Architecture:**
- [How to Build the Outbox Pattern in .NET](https://oneuptime.com/blog/post/2026-01-26-dotnet-outbox-pattern/view)
- [The Transactional Outbox Pattern: A Deep Dive](https://dev.to/rock_win_c053fa5fb2399067/the-transactional-outbox-pattern-a-deep-dive-into-reliable-messaging-in-distributed-systems-3ag0)

**Custom Fields & Schema:**
- [Entity–attribute–value model - Wikipedia](https://en.wikipedia.org/wiki/Entity%E2%80%93attribute%E2%80%93value_model)
- [Magento EAV database architecture](https://yegorshytikov.medium.com/magento-eav-database-architecture-20b25c09af98)

**Workflow & Automation:**
- [Workflow Engine vs. State Machine](https://workflowengine.io/blog/workflow-engine-vs-state-machine/)
- [Lead generation automation workflows: delivering results in 2026](https://monday.com/blog/crm-and-sales/lead-generation-automation/)

**Omnichannel Communication:**
- [Omnichannel Communication: Guide for 2026](https://chatarmin.com/en/blog/omnichannel-communication)
- [Omnichannel CRM Solutions](https://www.inogic.com/blog/2026/02/omnichannel-crm-solutions-boost-customer-engagement-with-whatsapp-sms-and-live-chat/)

**SaaS Onboarding:**
- [SaaS Onboarding Flows That Actually Convert in 2026](https://www.saasui.design/blog/saas-onboarding-flows-that-actually-convert-2026)
- [Architecture Patterns for SaaS Platforms](https://medium.com/appfoster/architecture-patterns-for-saas-platforms-billing-rbac-and-onboarding-964ea071f571)
