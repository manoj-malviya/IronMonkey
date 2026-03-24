# Roadmap: IronMonkey — Lead Management System

## Overview

The roadmap starts with an unbreakable multi-tenancy foundation, then builds the configurable lead data model that all downstream features depend on, adds the ingestion channels that fill the pipeline, implements the pipeline board and workflow engine that drive the actual lead management work, and finishes with the activity timeline and dashboards that let users understand what's happening. Each phase delivers a complete, verifiable capability before the next begins.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Multi-Tenancy Foundation** - Isolated database per tenant with bulletproof tenant context routing (completed 2026-03-20)
- [ ] **Phase 2: Configurable Lead Model** - Tenant-defined custom fields, pipeline stages, and duplicate detection (gap closure in progress)
- [ ] **Phase 3: Lead Ingestion** - Manual entry, CSV import, REST API, and embeddable web forms
- [x] **Phase 4: Pipeline & Workflow Engine** - Kanban board, task management, lead routing, and configurable automation rules (completed 2026-03-22)
- [ ] **Phase 5: Activity & Reporting** - Unified lead activity timeline and pipeline/performance dashboards

## Phase Details

### Phase 1: Multi-Tenancy Foundation
**Goal**: Any tenant can sign up and get a fully isolated database, with every subsequent request automatically routed to the correct tenant data
**Depends on**: Nothing (first phase)
**Requirements**: TNCY-01, TNCY-02
**Success Criteria** (what must be TRUE):
  1. A new tenant signup provisions a separate database and that database is reachable within the same request flow
  2. A user authenticated to Tenant A cannot retrieve any data belonging to Tenant B — verified by integration tests that attempt cross-tenant reads
  3. All API requests resolve the correct tenant database from JWT claims without any explicit per-request configuration
  4. Background jobs that touch tenant data carry tenant context — no job can run against a tenant database without that tenant being set in scope
**Plans**: 6 plans
Plans:
- [ ] 01-01-PLAN.md — Test scaffold (xUnit + TestContainers)
- [ ] 01-02-PLAN.md — Data layer: PostgreSQL, CentralDbContext, TenantDbContext
- [ ] 01-03-PLAN.md — Auth pipeline: JWT TenantId, ITenantService, LoginEndpoint
- [ ] 01-04-PLAN.md — Tenant provisioning workflow
- [ ] 01-05-PLAN.md — Hangfire background jobs + migration orchestration
- [ ] 01-06-PLAN.md — Final test completion + human verification

### Phase 2: Configurable Lead Model
**Goal**: A tenant can fully configure the shape of their lead and pipeline data — custom fields, stages, and duplicate rules — before any leads are created
**Depends on**: Phase 1
**Requirements**: LEAD-01, LEAD-02, LEAD-03, LEAD-04, LEAD-05
**Success Criteria** (what must be TRUE):
  1. Tenant admin can create custom fields on lead records (text, number, date, dropdown, multi-select, currency, boolean) and those fields appear on the lead form
  2. Tenant admin can define pipeline stages with custom names and ordering, and those stages are the only valid stages for leads in that tenant
  3. Every lead record stores its source (manual, import, API, web form) and that value is visible in the lead detail view
  4. Creating or importing a lead with a matching email, phone, or name surfaces a duplicate warning before the record is saved
  5. User can merge two duplicate lead records into one, with the surviving record retaining the complete history of both
**Plans**: 6 plans
Plans:
- [x] 02-01-PLAN.md — Wave 0 test stubs (5 integration test files for all LEAD requirements)
- [x] 02-02-PLAN.md — Data layer: Lead entity upgrade, PipelineStage, CustomFieldDefinition, LeadMerge entities + EF migration
- [x] 02-03-PLAN.md — API endpoints: custom fields CRUD, pipeline stage CRUD, lead creation with source
- [x] 02-04-PLAN.md — Duplicate detection service (FuzzySharp) + lead merge service with audit
- [x] 02-05-PLAN.md — Integration test implementation + human verification
- [x] 02-06-PLAN.md — Gap closure: register CheckDuplicatesEndpoint and MergeLeadsEndpoint in Endpoints.cs

### Phase 3: Lead Ingestion
**Goal**: Leads can enter the system through any channel — manual entry, bulk import, REST API, or web form — with consistent validation and source tracking
**Depends on**: Phase 2
**Requirements**: INGST-01, INGST-02, INGST-03, INGST-04
**Success Criteria** (what must be TRUE):
  1. User can create a lead by filling in a form in the UI and the lead appears in the pipeline immediately after saving
  2. User can upload a CSV file, see validation errors before committing, and import valid rows in bulk with bad rows skipped
  3. An external system can POST to the REST API with lead data and receive a created lead ID in response — the lead appears in the pipeline
  4. Tenant admin can generate an embeddable web form that, when submitted on an external website, creates a lead in the tenant's pipeline
**Plans**: 7 plans
Plans:
- [x] 03-01-PLAN.md — Wave 0 test stubs (7 test files for all INGST requirements)
- [x] 03-02-PLAN.md — Data layer: ApiKey, WebForm (central), ImportBatch (tenant) entities + EF migrations
- [x] 03-03-PLAN.md — API key service + REST API ingestion endpoint (INGST-03)
- [x] 03-04-PLAN.md — CSV import service + Hangfire job + import endpoints (INGST-02)
- [x] 03-05-PLAN.md — Web form service + hosted page + submission endpoint (INGST-04)
- [x] 03-06-PLAN.md — Wire all: DI registrations, endpoint registration, rate limiting, manual entry duplicate warning (INGST-01)
- [x] 03-07-PLAN.md — Integration test implementation + human verification of hosted form page

### Phase 4: Pipeline & Workflow Engine
**Goal**: Users can manage leads through a visual pipeline with task tracking, assignment routing, and automated rules that act on lead state changes
**Depends on**: Phase 3
**Requirements**: PIPE-01, PIPE-02, PIPE-03, PIPE-04, PIPE-05
**Success Criteria** (what must be TRUE):
  1. User can view all leads in a Kanban board grouped by pipeline stage and drag a lead card from one stage to another — the transition is validated against the tenant's configured allowed transitions
  2. User can create a task linked to a lead with a due date, priority level, and assignee, and the task appears in that assignee's task list
  3. Admin can configure rule-based lead routing (round-robin or territory) so that newly created leads are automatically assigned to the correct agent without manual intervention
  4. Tenant admin can define a workflow rule with a trigger (field change, status change, or time elapsed), conditions, and an auto-action (assign, notify, schedule follow-up) — and that action fires automatically when the trigger condition is met
  5. The system enforces configured state machine transitions — attempting an invalid lead status transition is rejected with a clear error
**Plans**: 6 plans
Plans:
- [x] 04-01-PLAN.md — Wave 0 test stubs (6 test files for all PIPE requirements)
- [x] 04-02-PLAN.md — Data layer: LeadTask, WorkflowRule, StageTransition, Notification, RoutingConfig entities + EF migration
- [x] 04-03-PLAN.md — State machine validation + Kanban board endpoints (PIPE-01, PIPE-05)
- [x] 04-04-PLAN.md — Task management endpoints + lead routing service (PIPE-02, PIPE-03)
- [x] 04-05-PLAN.md — Workflow rule engine + background jobs + DI/endpoint wiring (PIPE-04)
- [x] 04-06-PLAN.md — Integration test implementation + human verification

### Phase 5: Activity & Reporting
**Goal**: Users can see everything that happened on a lead in one place and understand pipeline health and team performance through dashboards
**Depends on**: Phase 4
**Requirements**: ACTV-01, REPT-01, REPT-02, REPT-03
**Success Criteria** (what must be TRUE):
  1. Opening a lead shows a unified activity timeline with all interactions, field changes, status transitions, tasks, and notes in chronological order
  2. The pipeline dashboard shows current lead count per stage and total deal value across the pipeline — updated without a page reload
  3. The conversion dashboard shows conversion rates broken down by stage, lead source, and selectable time period
  4. The agent performance dashboard shows each team member's lead count handled, tasks completed, and conversion rate for a selected period
**Plans**: 5 plans
Plans:
- [x] 05-01-PLAN.md — Wave 0 test stubs (5 integration test files for all Phase 5 requirements)
- [x] 05-02-PLAN.md — Data layer: ActivityLog entity, Opportunity.Amount field, EF migration + dashboard indexes
- [ ] 05-03-PLAN.md — ActivityChangeInterceptor, IActivityTrackingService, timeline endpoints (ACTV-01)
- [x] 05-04-PLAN.md — Pipeline, conversion, and agent performance dashboard endpoints (REPT-01, REPT-02, REPT-03)
- [ ] 05-05-PLAN.md — DI wiring, endpoint registration, integration test implementation + verification

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Multi-Tenancy Foundation | 6/6 | Complete   | 2026-03-20 |
| 2. Configurable Lead Model | 5/6 | Gap closure | - |
| 3. Lead Ingestion | 6/7 | In Progress|  |
| 4. Pipeline & Workflow Engine | 6/6 | Complete   | 2026-03-22 |
| 5. Activity & Reporting | 3/5 | In Progress|  |
