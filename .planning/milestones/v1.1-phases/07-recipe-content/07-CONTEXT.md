# Phase 7: Recipe Content - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Define and seed the Automobile Dealership and Educational Institution recipes with complete domain content (pipeline stages, custom fields, workflow rules, roles) plus sample lead data. After provisioning with either domain recipe, the tenant has a populated, working pipeline. Does not include recipe selection UI, preview endpoints, or admin CRUD (Phase 8).

</domain>

<decisions>
## Implementation Decisions

### Automobile Dealership Recipe
- **D-01:** Road-to-the-Sale pipeline stages (6 stages): Inquiry [Entry] -> Test Drive [Active] -> Negotiation [Active] -> F&I [Active] -> Sold [ClosedWon] -> Lost [ClosedLost]
- **D-02:** Core vehicle custom fields (6 fields): Vehicle Make [Dropdown: Toyota, Honda, Ford, Chevrolet, BMW, Mercedes, Other], Vehicle Model [Text], Vehicle Year [Number], Budget Range [Dropdown: Under 20K, 20-35K, 35-50K, 50-75K, 75K+], Has Trade-In [Boolean], Preferred Contact [Dropdown: Phone, Email, Text, WhatsApp]
- **D-03:** Follow-up workflow rules (2 rules): (1) "Notify on Negotiation" — StatusChange trigger, notify Sales Manager role when lead reaches Negotiation stage; (2) "Flag Stale Inquiry" — TimeElapsed trigger, flag leads idle in Inquiry 48+ hours as at-risk
- **D-04:** Standard dealership roles (3 roles): Sales Manager ("Oversees deals, approvals, and team performance"), Sales Executive ("Handles walk-ins, test drives, and closing deals"), BDC Agent ("Manages inbound inquiries and schedules appointments")
- **D-05:** Fixed GUID for Automobile recipe: `00000000-0000-0000-0000-000000000002`, IndustrySlug: `automobile`

### Educational Institution Recipe
- **D-06:** Admissions funnel pipeline stages (6 stages): Inquiry [Entry] -> Application [Active] -> Under Review [Active] -> Interview [Active] -> Enrolled [ClosedWon] -> Declined [ClosedLost]
- **D-07:** Core student custom fields (6 fields): Program of Interest [Dropdown: Engineering, Business, Arts, Science, Medicine, Law, Education, Other], Grade/Year Level [Dropdown: K-5, 6-8, 9-12, Undergraduate, Graduate], Previous School [Text], Guardian Name [Text], Guardian Phone [Text], Scholarship Needed [Boolean]
- **D-08:** Notification workflow rules (2 rules): (1) "Notify on Review" — StatusChange trigger, notify Admissions Officer role when lead moves to Under Review; (2) "Flag Stale Inquiry" — TimeElapsed trigger, flag leads idle in Inquiry 7+ days as at-risk
- **D-09:** Standard admissions roles (3 roles): Admissions Director ("Oversees admissions process and team performance"), Admissions Officer ("Reviews applications and conducts interviews"), Academic Counselor ("Guides prospective students through program selection")
- **D-10:** Fixed GUID for Education recipe: `00000000-0000-0000-0000-000000000003`, IndustrySlug: `education`

### Sample Lead Data
- **D-11:** 5 sample leads per domain recipe, spread across active pipeline stages (2 in Entry, 1 each in subsequent Active stages). No leads in terminal stages (ClosedWon/ClosedLost)
- **D-12:** Realistic placeholder names and data — real-sounding but clearly fictional. Use `@example.com` emails and `+1-555-0xxx` phone numbers. Diverse names representing different backgrounds
- **D-13:** Sample leads include custom field values populated with domain-appropriate data (e.g., Vehicle Make: Honda, Program: Engineering)
- **D-14:** Lead source for all sample leads: `WebForm` (most common real-world source)

### Recipe Seeding Strategy
- **D-15:** Domain recipes seeded via EF Core migration (same InsertData pattern as Blank recipe in Phase 6). Fixed GUIDs ensure deterministic provisioning
- **D-16:** Single migration `SeedDomainRecipes` inserts both Automobile and Education recipes in one migration
- **D-17:** Add `SampleLeadDefinition` DTO to `RecipeContentModel` — new `SampleLeads` section with firstName, lastName, email, mobile, source, stageName, customFieldValues fields
- **D-18:** `TenantProvisioningService.SeedTenantDataAsync` extended to process `SampleLeads` section — creates Lead entities mapped to the correct pipeline stages by name matching

### Claude's Discretion
- Exact sample lead names, emails, and phone numbers (following D-12 guidelines)
- Custom field values for each sample lead (following D-13 guidelines)
- ConditionJson and ActionJson structure for workflow rules (following existing WorkflowRule patterns)
- SampleLeadDefinition DTO design details (property types, nullability)
- Migration file naming and timestamp
- Test structure and assertions for recipe content verification
- Order of operations for seeding leads (after stages are created, so PipelineStageId can be resolved)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Recipe content requirements
- `.planning/REQUIREMENTS.md` §Recipe Content — RCNT-01, RCNT-02, RCNT-03 definitions
- `.planning/ROADMAP.md` §Phase 7 — Success criteria (4 items) defining what must be TRUE

### Upstream phase artifacts
- `.planning/phases/06-recipe-data-model/06-CONTEXT.md` — All 15 recipe infrastructure decisions (entity structure, application strategy, versioning, Blank recipe design)
- `.planning/phases/06-recipe-data-model/06-VERIFICATION.md` — Phase 6 verification confirming infrastructure works

### Key existing code references
- `IronMonkey.Data/Entities/IndustryRecipe.cs` — Recipe entity with Create/UpdateContent methods, ContentJson JSONB storage
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` — Current DTO structure (PipelineStageDefinition, CustomFieldDefinitionDto, WorkflowRuleDefinition, RoleDefinition) — needs SampleLeadDefinition added
- `IronMonkey.Data/CentralDbContext.cs` — DbSet<IndustryRecipe> registration
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` — SeedTenantDataAsync method that processes recipe content — needs sample lead processing added
- `IronMonkey.Data/Entities/Lead.cs` — Lead.Create() factory method signature: (tenantId, firstName, lastName, mobile, email, source, pipelineStageId)
- `IronMonkey.Data/Entities/PipelineStage.cs` — StageType enum (Entry, Active, ClosedWon, ClosedLost), PipelineStage.Create() factory
- `IronMonkey.Data/Entities/CustomFieldDefinition.cs` — CustomFieldType enum, CustomFieldDefinition.Create() factory
- `IronMonkey.Data/Entities/WorkflowRule.cs` — WorkflowTrigger enum (FieldChange, StatusChange, TimeElapsed), WorkflowRule.Create() factory
- `IronMonkey.Data/Entities/Role.cs` — Role.Create(int id, string name) factory, existing role IDs (1, 201, 301, 302)
- `IronMonkey.Data/Migrations/Central/` — Existing Blank recipe seed migration as pattern reference

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `RecipeContentModel` — Already has 4 sections (PipelineStages, CustomFields, WorkflowRules, Roles). Extending with SampleLeads is additive
- `TenantProvisioningService.SeedTenantDataAsync` — Already processes stages, fields, rules from recipe content. Adding lead processing follows the same pattern
- Blank recipe seed migration — Pattern for InsertData with fixed GUID and PascalCase ContentJson
- `Lead.Create()` factory — Established pattern for creating leads with all required fields

### Established Patterns
- Recipe content JSONB with PascalCase keys (critical — System.Text.Json default)
- Fixed GUIDs for deterministic recipes: Blank=`...001`, Automobile=`...002`, Education=`...003`
- InsertData in migrations for recipe seeding
- Entity factory methods: `Entity.Create(...)` for all domain entities
- Dependency ordering in SeedTenantDataAsync: roles -> stages -> fields -> rules -> (now) leads

### Integration Points
- `RecipeContentModel.cs` — Add SampleLeadDefinition class and SampleLeads property
- `TenantProvisioningService.cs` — Add lead seeding logic after stages are created (needs stage name -> ID mapping)
- `IronMonkey.Data/Migrations/Central/` — New migration with InsertData for both domain recipes
- `IronMonkey.Tests/Integration/` — New tests verifying domain recipe provisioning creates correct entities

</code_context>

<specifics>
## Specific Ideas

### Automobile Dealership — Exact Content
**Stages:** Inquiry, Test Drive, Negotiation, F&I, Sold, Lost
**Fields:** Vehicle Make (Dropdown: Toyota/Honda/Ford/Chevrolet/BMW/Mercedes/Other), Vehicle Model (Text), Vehicle Year (Number), Budget Range (Dropdown: Under 20K/20-35K/35-50K/50-75K/75K+), Has Trade-In (Boolean), Preferred Contact (Dropdown: Phone/Email/Text/WhatsApp)
**Rules:** "Notify on Negotiation" (StatusChange -> Negotiation -> notify Sales Manager), "Flag Stale Inquiry" (TimeElapsed -> Inquiry 48h -> flag at-risk)
**Roles:** Sales Manager, Sales Executive, BDC Agent
**Sample Leads:** 5 leads — realistic names, @example.com emails, +1-555-0xxx phones, WebForm source, spread across Inquiry(2)/Test Drive(1)/Negotiation(1)/F&I(1)

### Educational Institution — Exact Content
**Stages:** Inquiry, Application, Under Review, Interview, Enrolled, Declined
**Fields:** Program of Interest (Dropdown: Engineering/Business/Arts/Science/Medicine/Law/Education/Other), Grade/Year Level (Dropdown: K-5/6-8/9-12/Undergraduate/Graduate), Previous School (Text), Guardian Name (Text), Guardian Phone (Text), Scholarship Needed (Boolean)
**Rules:** "Notify on Review" (StatusChange -> Under Review -> notify Admissions Officer), "Flag Stale Inquiry" (TimeElapsed -> Inquiry 7d -> flag at-risk)
**Roles:** Admissions Director, Admissions Officer, Academic Counselor
**Sample Leads:** 5 leads — realistic names, @example.com emails, +1-555-0xxx phones, WebForm source, spread across Inquiry(2)/Application(1)/Under Review(1)/Interview(1)

</specifics>

<deferred>
## Deferred Ideas

- Recipe selection during signup UI — Phase 8 scope (ONBD-01)
- Recipe preview endpoint — Phase 8 scope (ONBD-02)
- Admin API for recipe CRUD — Phase 8 scope (RADM-01, RADM-02, RADM-03)
- Recipe upgrade/migration for existing tenants — v1.2 (Out of Scope)
- Additional industry recipes beyond Automobile and Education — future milestone

</deferred>

---

*Phase: 07-recipe-content*
*Context gathered: 2026-03-26*
