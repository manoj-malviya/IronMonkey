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

## Cross-Milestone Trends

| Metric | v1.0 |
|--------|------|
| Phases | 5 |
| Plans | 30 |
| Tasks | 59 |
| LOC | 19,106 |
| Test Count | 108+ |
| Duration | 6 days |
