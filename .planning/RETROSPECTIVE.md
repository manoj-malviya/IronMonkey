# Project Retrospective

## Milestone: v1.0 — MVP

**Shipped:** 2026-03-24
**Phases:** 5 | **Plans:** 30 | **Tasks:** 59

### What Was Built
- Database-per-tenant isolation with JWT-based automatic routing
- Configurable lead model with JSONB custom fields, pipeline stages, duplicate detection
- Multi-channel lead ingestion: manual entry, CSV import, REST API, embeddable web forms
- Pipeline workflow engine: Kanban board, task management, round-robin/territory routing, automation rules
- Activity timeline (SaveChanges interceptor) and 3 reporting dashboards (pipeline, conversion, agent performance)

### What Worked
- Wave-based parallel execution cut wall-clock time significantly (2 agents per wave)
- Test-first approach (Wave 0 stubs) caught integration issues early
- EF Core global query filters eliminated entire class of cross-tenant data leaks
- Outbox pattern established in Phase 1 became template for all background work
- JSONB HasConversion pattern from Phase 2 reused cleanly in Phase 5 (ActivityLog)

### What Was Inefficient
- Merge conflicts from parallel Wave 2 agents (Endpoints.cs touched by both 05-03 and 05-04)
- Some SUMMARY.md one-liners were generic ("One-liner:") rather than descriptive
- Phase 2/3 roadmap status showed "gap closure"/"in progress" even after completion on phase4 branch
- Worktree cleanup needed manual intervention (.claude/worktrees/ accidentally committed)

### Patterns Established
- `IEndpoint` with static `Map()`, nested Request/Response records, nested `RequestValidator`
- `Entity.Create()` static factory pattern on all domain entities
- `ITenantDbContextFactory.CreateForTenant()` for per-request tenant context
- Hangfire `[Queue("tenant")]` with TenantId as first parameter
- Integration tests using Testcontainers `PostgreSqlFixture` with unique DB per test

### Key Lessons
- Always add `.claude/worktrees/` to `.gitignore` before parallel execution
- EF Core 10 + Npgsql requires DateTimeKind.Utc explicitly — Unspecified rejected at runtime
- Non-nullable FK constraints require real entity references in tests (not random Guids)
- HasConversion JSONB requires explicit `IsModified = true` after mutating dictionary references

### Cost Observations
- Model mix: ~30% opus (orchestration), ~50% sonnet (execution), ~20% haiku (research/verification)
- Sessions: ~8 across 6 days
- Notable: Parallel Wave execution (2 agents) roughly halved per-wave wall-clock time

---

## Milestone: v1.1 — Tenant Onboarding with Industry Recipes

**Shipped:** 2026-03-27
**Phases:** 3 | **Plans:** 9 | **Tasks:** 16

### What Was Built
- IndustryRecipe entity with JSONB content model storing pipeline stages, custom fields, workflow rules, and default roles
- Blank/Custom recipe with fixed GUID for deterministic fallback provisioning
- Automobile Dealership and Educational Institution domain recipes with sample lead seeding
- Recipe selection integrated into tenant signup-to-provision flow with deactivated-recipe guard
- 5 recipe API endpoints (list, preview, create, update, deactivate) with content validation
- 14 RecipeEndpointTests covering all 8 Phase 8 requirements

### What Worked
- JSONB content model design allowed recipes to be self-contained snapshots — no FKs back to templates
- Fixed GUID for Blank recipe eliminated DB lookups during provisioning fallback
- Split SaveChangesAsync (flush stages first, then seed leads) cleanly solved stage ID resolution
- AllowAnonymous on read endpoints / RequireAuthorization on mutations — clean auth split
- Reusing existing integration test patterns (PostgreSqlFixture) made Phase 8 tests straightforward

### What Was Inefficient
- Some Phase 7 SUMMARY.md one-liners were still generic ("One-liner:") — same issue from v1.0
- ROADMAP.md checkboxes not kept in sync with actual completion (showed unchecked despite plans done)
- Domain recipe field definitions flagged as needing expert validation but proceeded without it
- Designer.cs files for EF migrations had to be manually created — error-prone pattern

### Patterns Established
- Recipe content model: `RecipeContentModel` with `StageDefinition[]`, `FieldDefinition[]`, `RuleDefinition[]`, `RoleDefinition[]`, `SampleLeadDefinition[]`
- Platform-level entities extend `Entity` (not `BaseTenantEntity`) — no TenantId
- Public/admin route group split on same prefix for mixed auth policies
- `RecipeContentValidator` validates JSONB structure before persistence

### Key Lessons
- EF Core `ApplyConfigurationsFromAssembly` includes ALL entity configs — tenant DBs mirror central DB shapes even for entities not logically tenant-scoped
- PascalCase JSON keys in JSONB are critical when using System.Text.Json default serialization
- Migration Designer.cs files required for `MigrateAsync()` to locate and apply migrations at runtime
- Two-phase SaveChanges needed when seeded entities have cross-references (stages before leads)

### Cost Observations
- Model mix: ~20% opus (orchestration), ~60% sonnet (execution), ~20% haiku (research)
- Sessions: ~3 across 1 day
- Notable: Entire v1.1 milestone completed in a single day — benefit of established patterns from v1.0

---

## Milestone: v1.2 — Admin UI

**Shipped:** 2026-04-02
**Phases:** 5 | **Plans:** 16 | **Tasks:** 20

*(Retrospective not captured at time of completion — see MILESTONES.md for details)*

---

## Milestone: v1.3 — Public Landing & Tenant Signup

**Shipped:** 2026-04-03
**Phases:** 2 | **Plans:** 6 | **Tasks:** 10

### What Was Built
- PublicLayout.razor — clean public layout shell (no admin sidebar) with sticky nav and dark footer
- Responsive SaaS landing page at / — dark gradient hero, tagline, dual CTAs, 4-feature grid
- Public routing: Routes.razor allows /, /login, /signup, /signup/success without auth redirect
- Tenant signup form at /signup with 7 validated fields, recipe browser with toggle-select preview
- SignupSuccessPage at /signup/success with confirmation message and navigation links
- Bidirectional login/signup cross-links completing full public navigation flow

### What Worked
- PublicLayout.razor as separate layout cleanly separated public and admin concerns — no conditional rendering
- Reusing Login.razor patterns (EditForm, DataAnnotationsValidator, HttpClientFactory) made signup form fast to build
- UI-SPEC design contracts locked visual decisions before planning — zero design drift during execution
- All backend APIs already existed (POST /auth/signup, GET /api/recipes) — pure frontend milestone
- Wave-based execution: Wave 1 (infrastructure) → Wave 2 (pages in parallel) — clean dependency flow

### What Was Inefficient
- v1.2 milestone retrospective was not captured — lost context on Admin UI lessons
- Recipe preview panel defines its own RecipePreviewResponse record instead of sharing a DTO — minor duplication

### Patterns Established
- Dual layout architecture: PublicLayout (public pages) vs MainLayout (admin pages) via @layout directive
- FeatureCard.razor with MarkupString Icon parameter for inline SVG — reusable marketing component
- RecipePreviewCard/RecipePreviewPanel as self-contained components with EventCallback wiring
- Path-based auth exclusion in Routes.razor NotAuthorized block for public routes

### Key Lessons
- Tailwind dark gradient hero (from-indigo-950 to-indigo-600) creates strong visual identity with minimal effort
- Blazor @layout directive is the cleanest way to switch layouts — no conditional rendering needed
- Self-contained record types inside components (RecipePreviewResponse) simplify API deserialization without shared DTOs
- Small milestones (2 phases) can complete in a single session when patterns are established

### Cost Observations
- Model mix: ~15% opus (orchestration), ~65% sonnet (execution), ~20% haiku (research/verification)
- Sessions: 1 (entire milestone in single session)
- Notable: Fastest milestone yet — 2 phases completed and verified in one session

---

## Cross-Milestone Trends

| Metric | v1.0 | v1.1 | v1.2 | v1.3 |
|--------|------|------|------|------|
| Phases | 5 | 3 | 5 | 2 |
| Plans | 30 | 9 | 16 | 6 |
| Tasks | 59 | 16 | 20 | 10 |
| LOC | 19,106 | ~28,000 | ~30,000+ | ~636 new |
| Test Count | 108+ | 143+ | 143+ | 143+ (no new tests) |
| Duration | 6 days | 1 day | ~2 days | 1 session |
