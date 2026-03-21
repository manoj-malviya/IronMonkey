# IronMonkey Architecture Research — Multi-Tenant Lead Management SaaS

**Research Date:** 2026-03-19
**Research Type:** Architecture dimension for multi-tenant system
**Confidence Level:** HIGH

---

## Quick Start: What to Read

### For Roadmap Planning
1. Start with **ARCHITECTURE_SUMMARY.md** — Executive overview of the three-layer architecture and phase ordering
2. Read **ARCHITECTURE.md** (Section: "Build Order Dependencies") — Specific component sequencing
3. Cross-reference **PITFALLS.md** — What can go wrong in each phase

### For Implementation
1. **ARCHITECTURE.md** — Complete reference for component boundaries, data flow, and patterns
2. **STACK.md** — Technology choices and why
3. **FEATURES.md** — What goes in each phase

### For Architecture Decisions
1. **ARCHITECTURE.md** sections:
   - "Component Boundaries" — What owns what
   - "Anti-Patterns to Avoid" — What NOT to do
   - "Scalability Considerations" — Trade-offs as you grow

---

## Document Map

| Document | Purpose | For Whom | Key Sections |
|----------|---------|----------|--------------|
| **ARCHITECTURE_SUMMARY.md** | Executive summary with phase recommendations | Roadmap planner, architect | Implications for Roadmap, Phase Structure |
| **ARCHITECTURE.md** | Complete architectural reference | Developers, architects | Component Boundaries, Data Flow, Patterns, Anti-Patterns |
| **STACK.md** | Technology recommendations | Tech leads, architects | Core Framework, Database, Infrastructure |
| **FEATURES.md** | What to build in each phase | Product, roadmap | Table Stakes, Differentiators, MVP Recommendation |
| **PITFALLS.md** | Mistakes to avoid | All phases | Critical/Moderate/Minor pitfalls, Phase-Specific Warnings |
| **SUMMARY.md** | Stack-focused summary (from earlier research) | Reference | Stack decisions, confidence breakdown |

---

## Architecture at a Glance

```
┌─────────────────────────────────────────────┐
│   PRESENTATION (Blazor Server + MudBlazor)  │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  ORCHESTRATION (Shared Master Database)      │
│  Tenants, Users, Recipes, Configuration      │
└──────────────┬──────────────────────────────┘
               │
         ┌─────┴─────┬─────────┬──────────┐
         │           │         │          │
    [TENANT1]   [TENANT2] [TENANT3]  [...TENANTn]
    Database    Database  Database    Database
    ├─ Leads    ├─ Leads   ├─ Leads    ├─ Leads
    ├─ Fields   ├─ Fields  ├─ Fields   ├─ Fields
    ├─ Workflows├─Workflows├─Workflows ├─Workflows
    └─ Events   └─ Events  └─ Events   └─ Events
         │           │         │          │
         └─────┬─────┴─────────┴──────────┘
               │
    ┌──────────▼──────────────────────────┐
    │  EVENT-DRIVEN SERVICES (Background)  │
    │  ├─ Workflow Trigger Evaluator       │
    │  ├─ Event Relay                      │
    │  └─ Communication Dispatcher         │
    │      ├─ Email (SendGrid/SMTP)        │
    │      ├─ SMS (Twilio)                 │
    │      └─ WhatsApp (Twilio API)        │
    └─────────────────────────────────────┘
```

## Critical Architectural Decisions

| Decision | Choice | Why | Impact on Roadmap |
|----------|--------|-----|-------------------|
| **Multi-tenancy** | Database-per-tenant | Maximum isolation, compliance | Tenant provisioning is Phase 1 blocker |
| **DbContext** | Scoped (not Singleton) | Tenant resolution per request | Must implement correctly in Phase 1 |
| **Custom Fields** | Hybrid relational + EAV | Standard columns + sparse custom | Affects Phase 2 data model design |
| **Workflows** | Event-driven async (not sync) | Fault tolerance, no timeouts | Requires Outbox pattern in Phase 1 |
| **Communications** | Message adapters | Swappable providers, centralized | Abstracts Twilio/SendGrid in Phase 4 |

---

## Phase Dependencies

```
PHASE 1: Provisioning & Routing
└─ Enables: Can create tenant, route to correct DB
   └─ PHASE 2: Configurable Leads
      └─ Enables: Custom fields, lead ingestion
         └─ PHASE 3: Workflow Engine
            └─ Enables: Automated lead routing, actions
               └─ PHASE 4: Omnichannel Comms
                  └─ Enables: Notifications via email/SMS/WhatsApp
                     └─ PHASE 5: UI & Onboarding
                        └─ Enables: Self-service tenant setup
                           └─ PHASE 6: Production Scale
                              └─ Enables: 1K-10K tenants
```

Each phase **blocks** the next from starting. Don't start Phase 2 until Phase 1 provisioning works.

---

## Key Implementation Patterns

### 1. Scoped DbContext + Tenant Routing
DbContext is **Scoped** (not Singleton), resolved per HTTP request. ITenantService extracts TenantId from JWT; DbContext.OnConfiguring looks up connection string based on tenant. Enables true multi-database isolation.

### 2. Hybrid Relational + EAV
Standard columns (Name, Email, Phone, Status) are relational for performance. Custom tenant-defined fields go in CustomFieldValues EAV table. No schema migrations per tenant. Proven at scale by Salesforce.

### 3. Outbox Pattern → Event-Driven
Domain events stored in Outbox table (same transaction as business data). Background service polls Outbox, routes to workflow evaluator or communication dispatcher. Guarantees "exactly once" delivery; survives failures.

### 4. Rule Engine for Workflows
WorkflowDefinitions stored as JSON. Conditions evaluated at runtime (IF budget >= 50000 AND source == "web"). Actions: assign, notify, schedule, update field. No code changes needed for tenant customization.

### 5. Message Adapters for Omnichannel
Abstract IMessageAdapter for Email, SMS, WhatsApp. Easy to swap providers (SendGrid → AWS SES) without changing business logic. Centralized error handling and retry logic.

---

## Pitfalls to Avoid

**Phase 1:**
- ❌ Singleton DbContextFactory (all users share same connection string)
- ❌ Hardcoded connection strings (not tenant-aware)
- ❌ Missing Outbox pattern (events lost on failure)

**Phase 2:**
- ❌ Storing all custom fields as JSON blobs (can't query efficiently)
- ❌ EAV table without indexes (slow queries)
- ❌ No field validators (garbage data)

**Phase 3:**
- ❌ Synchronous workflow execution (request timeouts)
- ❌ Workflow actions without retry logic (failures cascade)
- ❌ No state machine validation (invalid status transitions)

**Phase 4:**
- ❌ Direct API calls to Twilio/SendGrid scattered in code (hard to test, swap providers)
- ❌ No idempotency checking (duplicate messages sent)
- ❌ Unencrypted API keys in config (security breach)

**Phase 5:**
- ❌ Complex onboarding without progress tracking (users quit)
- ❌ No help/support contact (abandoned tenants)

**Phase 6:**
- ❌ No connection pooling strategy (performance degrades at 1K+ tenants)
- ❌ No migration automation (schema updates fail on some tenants)
- ❌ No audit logging (compliance violations)

See **PITFALLS.md** for detailed mitigation strategies.

---

## Research Confidence by Area

| Area | Confidence | Reason |
|------|-----------|--------|
| **Database-per-Tenant** | HIGH | Microsoft Learn official guidance; proven by HubSpot, Salesforce |
| **DbContext Routing** | HIGH | EF Core documentation + community patterns |
| **Custom Fields (EAV)** | HIGH | Proven at scale; clear trade-offs |
| **Event-Driven & Outbox** | HIGH | Microservices.io patterns; Azure guidance |
| **Workflow Rule Engine** | HIGH | 2026 CRM standard; simple evaluators well-understood |
| **Omnichannel (Twilio/SendGrid)** | HIGH | Industry-standard APIs; stable SDKs |
| **Provisioning at Scale** | MEDIUM | Architecture solid; platform-specific details TBD |
| **SignalR Scaling (Blazor Server)** | MEDIUM | Fine to 1K concurrent; beyond requires Azure SignalR Service |

---

## What Changed From Previous Research?

Earlier research (SUMMARY.md, STACK.md) covered **technology stack** (PostgreSQL, EF Core, Twilio). This research adds **architecture dimension** — specifically:
- How to structure the system across 3 layers
- Component boundaries and communication
- Data flow end-to-end
- Build order and dependencies
- Patterns and anti-patterns specific to multi-tenant configurable systems

The two research tracks are complementary: technology choices inform architecture, architecture informs implementation patterns.

---

## Using This Research for Roadmap Creation

### For Phase Definition
1. Read ARCHITECTURE_SUMMARY.md → "Phase Structure Recommended"
2. For each phase, cross-check FEATURES.md → what's the deliverable?
3. Cross-check PITFALLS.md → what can go wrong in this phase?
4. Add mitigations to the phase description

### For Build Sequencing
1. Read ARCHITECTURE.md → "Suggested Build Order"
2. Group components by phase based on dependencies
3. Identify "Phase 1 blockers" that must be finished before Phase 2 starts

### For Success Criteria
For each phase, define:
- **What's the deliverable?** (e.g., "Can create tenant and route requests to correct DB")
- **How do we test it?** (e.g., "Create 3 test tenants, verify no data leakage")
- **What's the failure case?** (e.g., "Request goes to wrong tenant's database")

---

## Next Steps for Roadmap

1. **Lock architecture decisions** from this research
   - Confirm: Database-per-tenant? Scoped DbContext? Outbox pattern?
   - These decisions are load-bearing; don't change them later

2. **Design Phase 1 in detail**
   - Tenant provisioning automation (what tool? PowerShell, Terraform, custom service?)
   - ITenantService + TenantResolutionMiddleware implementation
   - Outbox schema and SaveChangesAsync override

3. **Identify Phase 1 blockers**
   - Database platform choice (affects provisioning automation)
   - Connection string management strategy
   - Test plan for tenant isolation (no cross-tenant data leakage)

4. **Validate architecture assumptions**
   - Prototype DbContextFactory with Scoped + dynamic routing
   - Test Outbox pattern with sample domain events
   - Verify event relay polling latency (how often should it poll?)

5. **Plan production hardening**
   - Start Phase 6 research in parallel with Phase 5
   - Identify connection pool strategy before Phase 1 ships
   - Plan migration automation early

---

## Sources

See each document (ARCHITECTURE.md, STACK.md, PITFALLS.md) for complete source citations.

**Key Sources for This Architecture:**
- [Multi-tenancy - EF Core | Microsoft Learn](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [How to Build the Outbox Pattern in .NET](https://oneuptime.com/blog/post/2026-01-26-dotnet-outbox-pattern/view)
- [Entity–attribute–value model](https://en.wikipedia.org/wiki/Entity%E2%80%93attribute%E2%80%93value_model)
- [Workflow Engine vs. State Machine](https://workflowengine.io/blog/workflow-engine-vs-state-machine/)
- [Omnichannel Communication: Guide for 2026](https://chatarmin.com/en/blog/omnichannel-communication)
