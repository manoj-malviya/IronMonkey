# Project Research Summary

**Project:** IronMonkey v1.1 (Industry Recipes)
**Domain:** Multi-tenant Lead Management SaaS with database-per-tenant isolation
**Researched:** 2026-03-24
**Confidence:** HIGH (existing validated v1.0 foundation; v1.1 extensions use proven patterns)

## Executive Summary

IronMonkey v1.1 adds industry-specific recipe templates to the v1.0 multi-tenant CRM foundation, reducing tenant time-to-first-lead from 2-3 hours to 15-20 minutes. The existing .NET 10.0 + EF Core 10.0.5 + PostgreSQL stack requires **zero new external dependencies** — recipes leverage JSONB support already proven for custom fields. The core challenge is not technology, but operational correctness: recipe application must be atomically transacted, schema-consistent across industry domains, and fully customizable post-provisioning. Success depends on preventing six critical pitfalls during the provisioning foundation (idempotency, data model clarity, field-type accuracy, partial-failure handling, blank-template usability, and version management). The recommended approach is to ship two domain-specific recipes (Automobile, Education) with industry expert validation, a minimal blank/custom option, and explicit recipe versioning from day one.

## Key Findings

### Recommended Stack

**No new dependencies required.** All v1.1 needs already exist in v1.0:

- **Entity Framework Core 10.0.5** — JSONB support via `HasConversion()` for storing recipe schema
- **Npgsql 10.0.1** — PostgreSQL JSONB native type mapping (critical for recipe JSON storage)
- **PostgreSQL 15-alpine** — JSONB native support for recipe data; proven in v1.0 custom fields
- **Newtonsoft.Json 13.0.3 + System.Text.Json** — Recipe schema serialization/deserialization
- **.NET 10.0 Aspire 13.1.0** — No changes; orchestration layer already handles multi-tenant provisioning
- **FluentValidation 12.1.1** — Recipe selection and seeding validation (already integrated)

**Key additions:** Recipe entity (`IndustryRecipe`) in `CentralDbContext`, optional recipe ID parameter to `TenantProvisioningService`, recipe application service to deserialize JSONB and create tenant entities during provisioning.

**Not added:** JSON Schema validators, template engines (Liquid/Scriban), GraphQL, custom DSLs, recipe marketplace, versioning libraries. Recipes stored as immutable JSONB snapshots; deserialization + EF entity creation sufficient.

### Expected Features

**Must have (table stakes) for v1.1:**

- **Recipe selection at signup** — Industry dropdown (Automobile, Education, Blank) during account creation
- **Pre-configured pipeline stages** — Automobile: Lead, Contact Attempt, Appointment, Test Drive, Negotiation, Sold, Lost (7 stages); Education: Inquiry, Application, Under Review, Interview, Interviewed, Offer, Enrolled, Rejected (8 stages)
- **Pre-configured custom fields** — Automobile: Vehicle Interest, Budget, Trade-in Value, Financing, Insurance Provider, Test Drive Date, Finance Source; Education: Degree Program, Test Scores, Education Level, Application Status, Interview Date, Offer, Expected Term, International Student
- **Pre-configured workflow rules** — Automobile: auto-assign leads via round-robin, email notification on stage change, schedule 24h follow-up post-test-drive, escalate 7-day negotiation stalls, auto-archive lost leads after 90 days; Education: email on status change, schedule interview 5 business days after application, escalate 30-day review bottlenecks, auto-assign by degree program, email offer letter, archive enrolled students
- **Recipe application during provisioning** — Atomic seeding of stages, fields, rules, and roles to tenant database
- **Full post-provisioning customization** — All seeded data (stages, fields, rules, roles) fully modifiable; recipe is starting point only, not a guardrail
- **Blank/Custom recipe option** — Minimal starting point: one default "Lead" stage, Admin role, no fields, for industries without matching recipe

**Should have (competitive) for v1.1:**

- Recipe metadata (names, descriptions, icons) for improved discoverability
- Role seeding during provisioning (Admin, Sales Rep, Sales Manager per industry)

**Defer to v1.2+:**

- Recipe documentation and preview UI
- Sample data seeding for exploration
- Quick-start wizard for multi-step customization
- Recipe versioning and upgrade tooling
- Copy existing tenant config as template
- Industry suggestion logic at signup
- Recipe marketplace or community templates

### Architecture Approach

Multi-tenant architecture uses three layers: **Orchestration** (CentralDbContext with Tenants, Users, IndustryRecipes, OutboxMessages), **Tenant Data** (per-tenant isolated databases with Leads, CustomFields, PipelineStages, WorkflowRules, Roles), and **Event-Driven Communication** (outbox polling, workflow trigger evaluation, omnichannel dispatch). Recipe application happens at provisioning time: load recipe template from Orchestration DB, deserialize JSONB, create tenant entities in isolated database. All recipe infrastructure already exists — seeding is the primary new code.

**Major components:**

1. **Recipe Service** — Load recipe template from CentralDbContext, manage recipe registry and versioning
2. **Recipe Application Service** — Deserialize recipe JSONB, create PipelineStages, CustomFieldDefinitions, WorkflowRules, Roles in tenant DB
3. **Provisioning Service (extended)** — Accept optional `recipeId` parameter, call recipe application during tenant setup
4. **Scoped Tenant Resolution** — Extract tenant ID from JWT, route to correct database
5. **Hybrid Lead Entity** — Standard columns (Name, Email, Phone) + EAV table for custom fields (proven pattern from v1.0)

### Critical Pitfalls

1. **Non-Idempotent Recipe Application** — Duplicate records or partial seeding if recipe application fails mid-transaction. **Prevention:** Wrap entire seeding sequence in single database transaction; use existence checks before each insert; track idempotency tokens; test explicit failure scenarios at each step.

2. **Recipe Data Model Confusion** — Template in central DB mutates vs instances in tenant DB diverge. **Prevention:** Document ownership upfront (CentralDbContext = immutable templates, TenantDbContext = mutable instances); forbid foreign keys from tenant entities back to recipe template; copy recipe to tenant DB at provisioning, don't reference; design allows future versioning (Tenant.RecipeVersion column).

3. **Industry-Specific Field Type Mismatches** — Automobile recipe specifies "Vehicle Type" as enum, seeded as text; VIN seeded without unique constraint. **Prevention:** Define recipe field spec upfront (data type, enum values, constraints, stage requirements); validate spec against EF Core model before seeding; get domain expert review (dealership manager, admissions officer); test schema post-seeding; document field mappings in tenant UI.

4. **Partial Provisioning Failure Without Clear Error** — Recipe application fails at step 5 of 5; transaction rolls back; user sees "Provisioning failed" but doesn't know why or if retry will help. **Prevention:** Wrap in transaction; provide detailed error messages (distinguish constraint violations, foreign key errors, timeouts); return structured error response with failed step and retryability flag; log provisioning steps with state tracking.

5. **Blank/Custom Recipe Unusable Out of Box** — Tenant chooses blank option, gets empty database, can't create leads (no stages), can't assign users (no roles), support required. **Prevention:** Blank template seeds minimum (one default "New" stage, Admin role); provide setup wizard or "Starter" recipe option; validate minimum requirements before allowing provisioning; clearly communicate requirements in UI.

6. **Recipe Versioning Drift After Release** — Recipe 1.0 released to Tenants A, B, C; Recipe 1.1 released with new stages; new Tenant D has different recipe than A, B, C; data inconsistency. **Prevention:** Design versioning upfront (recipe has version field, Tenant table tracks RecipeVersion); make recipe updates additive when possible; provide upgrade path in tenant dashboard; document changelog; consider migration tooling for v1.2+.

## Implications for Roadmap

Based on research, IronMonkey v1.1 should organize around recipe-centric phases with clear provisioning foundation first, then domain-specific details, then hardening.

### Phase 1: Recipe Data Model & Provisioning Foundation
**Rationale:** Recipe application is a new, critical seeding path. Must be bulletproof before adding industry-specific recipes. Foundation supports all downstream work without rework.

**Delivers:**
- Recipe entity in CentralDbContext (stores IndustryRecipe templates as immutable JSONB)
- Recipe Application Service (deserializes JSONB, creates tenant entities atomically)
- Extended TenantProvisioningService (accepts recipeId parameter, applies recipe during provisioning)
- Transactional seeding with rollback safety
- Idempotency checks and detailed error handling

**Addresses features:**
- Full post-provisioning customization (infrastructure complete)
- Blank recipe option (minimal seeding logic)

**Avoids pitfalls:**
- Pitfall 1 (idempotency) — transactional seeding + existence checks
- Pitfall 2 (data model confusion) — clear ownership documented in code
- Pitfall 4 (partial failure) — transaction wrapping + error messaging

**Risk:** Seeding logic must handle dependency ordering (stages before rules that reference stages, roles before permissions). Test atomic rollback explicitly.

### Phase 2: Automobile & Education Recipe Definitions
**Rationale:** Domain-specific recipes depend on seeding infrastructure from Phase 1. Define both in parallel to validate pattern.

**Delivers:**
- Automobile recipe: 7 pipeline stages, 7 custom fields, 5 workflow rules, 3 roles
- Education recipe: 8 pipeline stages, 8 custom fields, 6 workflow rules, 3 roles
- Recipe field specification documents (validate data types, enum values, constraints against EF Core model)
- Domain expert review (auto dealership manager, admissions officer)
- Test suite: schema validation post-seeding, deduplication checks, stage/field count assertions

**Addresses features:**
- Recipe selection at signup (2 domain-specific + 1 blank option)
- Pre-configured pipeline stages (Automobile + Education)
- Pre-configured custom fields (Automobile + Education)
- Pre-configured workflow rules (Automobile + Education)
- Role seeding during provisioning (Admin, Sales Rep, Manager per industry)

**Avoids pitfalls:**
- Pitfall 3 (field type mismatches) — expert review + schema validation tests
- Pitfall 5 (blank template) — blank template definition + guidance UI
- Pitfall 6 (versioning drift) — tag recipes with version numbers from day one

**Risk:** Domain definitions may be incomplete without SME input. Plan 1-2 cycles of expert feedback before finalizing.

### Phase 3: Recipe Selection & Onboarding UI
**Rationale:** UI layer depends on recipe definitions being final. Recipe selection at signup gates provisioning service.

**Delivers:**
- Modified signup flow: industry dropdown (Automobile, Education, Blank) before account creation
- Recipe metadata display (name, description, icon, key fields preview)
- Post-signup messaging (what to expect with selected recipe)
- Blank template guidance ("You chose custom; here's what you need to set up")

**Addresses features:**
- Recipe selection at signup (UI implementation)
- Recipe metadata

**Implementation:** Minimal API changes — add optional `recipeId` to signup request. Provisioning service already routes to correct recipe via parameter.

### Phase 4: Testing & Provisioning Hardening
**Rationale:** Recipe seeding is complex; must be thoroughly tested before production. Integration tests exercise all failure modes.

**Delivers:**
- Integration tests: provision each recipe variant, verify schema matches spec
- Failure injection tests: simulate network timeout at step 2, verify rollback
- Idempotency tests: apply recipe twice, verify no duplicates
- Performance tests: provisioning latency < 5 seconds per recipe
- Monitoring and alerting (provisioning failures, partial states detected)

**Addresses pitfalls:**
- Pitfall 1 (idempotency) — explicit retry tests
- Pitfall 4 (partial failure) — failure injection tests

**Risk:** Requires Testcontainers (PostgreSQL per test). May be slow; consider parallel test execution.

### Phase 5: Documentation & Go-Live
**Rationale:** Late phase because depends on all implementations complete. Documentation is final step before customer access.

**Delivers:**
- Tenant documentation: "Your industry recipe" — describes seeded stages, fields, rules
- Admin documentation: Recipe maintenance guide, versioning strategy
- Support playbook: Common issues (wrong recipe selected, customization regrets)
- Recipe versioning policy: How future recipes will be released, upgrade paths

**Avoids pitfalls:**
- Pitfall 5 (blank template confusion) — documentation clarifies minimum requirements
- Pitfall 6 (versioning drift) — versioning policy set before release

### Phase Ordering Rationale

1. **Foundation first (Phase 1):** Seeding infrastructure is a pre-requisite for all recipe work. If this is broken, all recipes fail. Must be bulletproof before domain work.

2. **Parallel domain work (Phase 2):** Automobile and Education recipes can be defined in parallel; both use same seeding infrastructure. Validate pattern with first recipe before committing to second.

3. **UI depends on definitions (Phase 3):** Recipe selection UI is trivial; depends on final recipe specs. Don't build until Phase 2 complete.

4. **Testing validates everything (Phase 4):** Integration tests depend on all implementations. Can't validate provisioning fully until recipes exist and schema is defined.

5. **Documentation last (Phase 5):** Write after everything works. Documentation is final gate before launch.

6. **Not in v1.1:** Recipe versioning tooling (track versions, upgrade paths) deferred to v1.2. v1.1 recipes are immutable snapshots; tenants manually use newer recipes if desired.

### Research Flags

Phases likely needing deeper research during planning:

- **Phase 2 (Automobile + Education Recipes):** Domain knowledge required. Recommend 2-3 expert interviews per industry (dealership manager, education admissions officer) to validate field definitions and workflow rules. Risk is high if definitions are incomplete or inaccurate.

- **Phase 4 (Testing):** Database versioning and schema validation strategy may need research. How to introspect PostgreSQL schema post-provisioning to verify field types, constraints, indexes were seeded correctly?

Phases with standard patterns (skip `/gsd:research-phase`):

- **Phase 1 (Foundation):** Transactional seeding, EAV pattern, idempotency tokens all well-documented in EF Core + SaaS literature. No research needed.

- **Phase 3 (UI):** Minimal API changes, standard Blazor form pattern. No novel architecture.

- **Phase 5 (Documentation):** Standard SaaS onboarding documentation patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | v1.0 proven all patterns; v1.1 adds no external dependencies. EF Core 10.0.5 + Npgsql 10.0.1 version alignment verified. JSONB pattern already in use for custom fields. |
| Features | HIGH | Table stakes verified against HubSpot, Pipedrive, Salesforce onboarding. Recipe definitions aligned with industry research (automotive CRM docs, higher ed admissions workflows). Feature dependencies clear. |
| Architecture | HIGH | Multi-tenant, DB-per-tenant, outbox pattern, EAV hybrid model all proven in v1.0. Recipe application layered cleanly on existing provisioning service. No breaking changes needed. |
| Pitfalls | HIGH | Six pitfalls extracted from SaaS provisioning literature (Microsoft Learn, AWS), EF Core best practices, and v1.0 lessons learned. Prevention strategies are concrete and testable. |

**Overall confidence:** HIGH

### Gaps to Address

1. **Domain Expert Input (Phase 2):** Field definitions for Automobile and Education recipes need validation with actual practitioners. Current definitions are research-backed but not verified by dealership managers or admissions officers. **How to handle:** Plan 1-2 interview cycles during Phase 2 planning; adjust recipe definitions based on feedback before finalizing.

2. **PostgreSQL Schema Introspection (Phase 4):** How to programmatically validate seeded schema (field types, constraints, indexes) post-provisioning? EF Core migrations handle schema creation, but testing whether schema matches spec requires database reflection. **How to handle:** Research PostgreSQL information schema queries during Phase 4 planning; may use EF Core's `Database.GetDbConnection()` + reflection.

3. **Performance Baseline:** Recipe seeding with 50+ fields, 10+ workflow rules per tenant may have measurable latency impact during provisioning. Current assumption is < 5 seconds; needs validation. **How to handle:** Phase 4 integration tests should measure provisioning latency with realistic recipe sizes; if > 5 seconds, investigate bulk insert optimization or async seeding.

4. **Blank Template Guidance:** How to guide blank template users through minimum configuration without being prescriptive? Research suggests setup wizard or "Starter" template, but optimal UX is unclear. **How to handle:** Phase 3 planning should include UX research on blank template guidance; consider A/B testing "wizard" vs "guidance" approaches during beta.

5. **Recipe Versioning Future-Proofing:** Design assumes versioning will be added in v1.2. Current design captures version number but doesn't implement upgrade tooling. Risk is if versioning logic isn't thought through, v1.2 migration code could be fragile. **How to handle:** Phase 1 should document recipe versioning strategy upfront (even if not implemented); v1.2 roadmap should reserve 2-3 weeks for migration tooling.

## Sources

### Primary (HIGH confidence)

- **STACK.md:** EF Core 10.0.5 / Npgsql 10.0.1 compatibility, JSONB pattern validation, existing v1.0 dependencies
- **FEATURES.md:** CRM table stakes from Pipedrive/HubSpot/Salesforce comparison; automotive and education recipe definitions from domain-specific CRM research; onboarding impact metrics (30-day churn reduction from 15-20% to 7-10%)
- **ARCHITECTURE.md:** Multi-tenant DB-per-tenant patterns (Microsoft Learn, AWS SaaS Lens); orchestration/tenant layer boundaries; outbox pattern; EAV hybrid model; workflow trigger evaluation; component ownership
- **PITFALLS.md:** Transactional seeding pitfalls from EF Core best practices; data model ownership (template vs instance) from SaaS architecture literature; field-type accuracy from domain-specific CRM implementations; provisioning error handling from AWS SaaS guidance; blank template usability from SaaS onboarding research; recipe versioning from software product versioning patterns

### Secondary (MEDIUM confidence)

- Automotive CRM market research (Driftrock, Pipedrive automotive vertical, HubSpot automotive, LeadsBridge)
- Higher education admissions CRM research (Element451, Zoho Education, LeadSquared, Meritto)
- Multi-tenant SaaS data modeling (Bytebase, AWS Well-Architected Lens, Microsoft Azure SQL patterns)

### Tertiary (LOW confidence)

- Specific field enum values for Automobile recipe (research shows common fields, but regional variations may exist; plan domain expert review)
- Blank template usability metrics (research shows setup wizard reduces onboarding time, but no empirical data for IronMonkey's specific user base)

---

*Research completed: 2026-03-24*
*Ready for roadmap: yes*
