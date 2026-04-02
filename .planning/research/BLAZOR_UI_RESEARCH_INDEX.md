# Research Index: Blazor Server Admin UI Integration

**Milestone:** IronMonkey v1.2 — Admin UI Foundation
**Research Date:** 2026-03-27
**Research Type:** Domain pitfalls for adding Blazor Server + Tailwind CSS to existing .NET Aspire multi-tenant API

---

## Files in This Research

### 1. PITFALLS_BLAZOR_UI.md
**Complete catalog of pitfalls with prevention strategies**

- **4 Critical Pitfalls:**
  1. Silent user identity changes on circuit reconnect (allows cross-user data access)
  2. DbContext pooling breaks multi-tenant data isolation (data leaks between tenants)
  3. JWT token expiration + circuit reconnect = silent API failures (user can't complete tasks)
  4. HttpClient missing JWT token in Authorization header (all API calls fail with 401)

- **7 Moderate Pitfalls:**
  1. Tailwind CSS hot reload broken by dotnet watch (requires two terminals)
  2. Multiple Blazor circuits for same user = stale state + memory leaks
  3. Tenant context not validated in API endpoints (data crosses tenant boundaries)
  4. IDbContextFactory registration causes DI errors
  5. Blazor Server form performance with 200+ fields
  6. Tailwind CSS content paths miss .razor file extensions
  7. Blazor Server circuit state not preserved on reconnect

- **Phase-Specific Warnings:** Table mapping pitfalls to phases where they occur

- **Prevention Checklist:** 10-item checklist for Phase 1 implementation

**Use When:** Planning Phase 1 scope, designing testing strategy, code review

---

### 2. SUMMARY_BLAZOR_UI.md
**Executive summary for roadmap planning**

- **Key Findings:** By domain (Stack, Architecture, Critical Pitfalls, Moderate Pitfalls, Minor Pitfalls)
- **Confidence Assessment:** By research area (Critical Pitfalls HIGH, Token Management HIGH, Tailwind MEDIUM, Performance MEDIUM-HIGH)
- **Implications for Roadmap:** Why Phase 1 must solve critical pitfalls; what can defer to Phase 2-3
- **Gaps to Address:** Open questions for phase-specific research
- **Roadmap Alignment:** How pitfalls map to phase structure

**Use When:** Executive planning, sprint planning, identifying research blockers

---

### 3. IMPLEMENTATION_PATTERNS_BLAZOR.md
**Actionable code patterns for Phase 1 developers**

- **8 Implementation Patterns:**
  1. Circuit Identity Validation (ICircuitHandler)
  2. Token Refresh Handler (DelegatingHandler with 401 interception)
  3. DbContext Configuration (no pooling, multi-tenant)
  4. Tenant Validation in Endpoints (JWT claim extraction + filtering)
  5. Tailwind CSS Configuration (standalone CLI + MSBuild integration)
  6. HttpClient Configuration in Program.cs (typed client + BearerTokenHandler)
  7. Login Endpoint Design (JWT generation + refresh token storage)
  8. AuthenticationStateProvider for Blazor (auth state management)

- **Code Examples:** Each pattern includes full code snippet, registration, and unit tests
- **Phase 1 Checklist:** 10-item implementation checklist
- **Performance Baselines:** Establish metrics for form rendering, token refresh, WebSocket

**Use When:** Implementing features, code review, debugging integration issues

---

## How to Use These Files

### For Roadmap Planning
1. Read **SUMMARY_BLAZOR_UI.md** — Executive summary
2. Review **PITFALLS_BLAZOR_UI.md** critical pitfalls section
3. Map pitfalls to Phase 1 checklist in PITFALLS_BLAZOR_UI.md

### For Phase 1 Implementation
1. Start with **IMPLEMENTATION_PATTERNS_BLAZOR.md** section 1 (Circuit Handler)
2. Implement patterns in order: Circuit → Token Refresh → DbContext → Tenant Validation
3. Cross-reference **PITFALLS_BLAZOR_UI.md** for testing strategy
4. Use Phase 1 checklist to verify completion

### For Code Review
1. Use **PITFALLS_BLAZOR_UI.md** phase-specific warnings for checklist
2. Verify each endpoint follows pattern from **IMPLEMENTATION_PATTERNS_BLAZOR.md** section 4
3. Check tests cover scenarios from PITFALLS_BLAZOR_UI.md critical section

### For Architecture Decisions
1. Read **SUMMARY_BLAZOR_UI.md** confidence assessment
2. Review **PITFALLS_BLAZOR_UI.md** "Phase-Specific Warnings" table
3. Use implications to inform ordering and scope decisions

---

## Key Decisions Made by This Research

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Must use ICircuitHandler for identity validation | Silent reconnect identity change is high-severity security issue | Phase 1 auth foundation must include circuit handler |
| Never use AddDbContextPool with multi-tenant DbContext | Single configuration error breaks all tenant boundaries | DbContext registration locked to AddDbContext |
| Token refresh must be automatic (DelegatingHandler) | Manual token refresh degrades admin UX; automatic maintains sessions | HttpClient configuration non-negotiable |
| Two-terminal dev setup for Tailwind (dotnet watch + tailwind --watch) | Dotnet watch bypasses MSBuild; hot reload expectation must be managed | Documentation in CLAUDE.md required |
| Tenant validation in every endpoint (not just filters) | Defensive programming; multiple layers prevent data leaks | Code review checklist required |
| Prefer ProtectedSessionStorage over localStorage | HttpOnly cookie for sensitive tokens; server-side validation easier | Architecture decision affects auth design |

---

## Confidence Levels by Topic

| Topic | Confidence | Basis | Need Validation? |
|-------|------------|-------|------------------|
| Circuit identity validation | HIGH | GitHub issue #65272 (2026), security concern | No — security critical |
| DbContext pooling breaks isolation | HIGH | EF Core docs + production SaaS patterns | No — architectural principle |
| JWT + 401 refresh pattern | HIGH | Multiple 2026 sources, Microsoft Learn, LJBLab guide | No — standard pattern |
| Tailwind hot reload issue | MEDIUM-HIGH | GitHub #6907, #6931 discussions, multiple blog posts | Validate with local testing |
| Large form performance (200+ fields) | MEDIUM | Microsoft docs + syncfusion patterns; real-world case needed | Validate with benchmarks in Phase 2 |
| Blazor circuit state loss | MEDIUM | GitHub issues + Stack Overflow; 2026 solutions emerging | Validate with integration tests |

---

## Research Boundaries

**In Scope (Researched):**
- Blazor Server circuit lifecycle and security
- JWT authentication + token refresh in Blazor Server
- Multi-tenant context isolation in DbContext
- Tailwind CSS integration with Blazor build pipeline
- HttpClient configuration for Blazor Server
- Admin UI form patterns (basic to complex)

**Out of Scope (Not Researched):**
- Custom role/permission granularity (deferred to Phase 3)
- Blazor WebAssembly (project uses Blazor Server)
- End-user CRM pages (admin UI only in v1.2)
- Analytics or telemetry integration
- Billing/licensing logic

**Partially Covered (Recommended Phase 2 Depth):**
- Form state persistence (basic prevention mentioned; full pattern deferred)
- Multi-circuit state sync (detection pattern; Hub-based invalidation deferred)
- Performance optimization (baselines identified; optimization deferred)

---

## Next Steps After Research

**Immediate (Before Phase 1 starts):**
1. [ ] Validate ICircuitHandler implementation approach with team
2. [ ] Confirm JWT token refresh endpoint design (how to pass refresh token?)
3. [ ] Review two-terminal Tailwind setup with developers
4. [ ] Identify which pitfalls must be integration test cases

**During Phase 1:**
1. [ ] Implement patterns from IMPLEMENTATION_PATTERNS_BLAZOR.md
2. [ ] Verify each implementation against PITFALLS_BLAZOR_UI.md prevention checklist
3. [ ] Add integration tests for circuit reconnect, token expiration, tenant isolation
4. [ ] Document patterns in CLAUDE.md (two-terminal Tailwind, circuit handlers, etc.)

**Phase 2 Planning:**
1. [ ] Deep dive on form state persistence (localStorage + reconnect)
2. [ ] Design multi-circuit state invalidation (Hub-based or cache keys)
3. [ ] Establish performance baselines and optimization targets
4. [ ] Plan Virtualize component implementation for 200+ field forms

---

## Research Metadata

**Researcher:** Claude Code (Haiku 4.5)
**Research Depth:** 4 sources per query (WebSearch), 2-3 queries per topic area
**Source Types:** Microsoft Learn (official docs), GitHub issues (2026), blogs (production experience), community patterns
**Total Sources Referenced:** 40+
**Research Time:** Single session, comprehensive coverage

**Validation Approach:**
- Official docs (Microsoft Learn) = HIGH confidence
- Recent GitHub issues (2026) = HIGH confidence
- Multiple independent sources agreeing = increase confidence one level
- Single source only = LOW confidence, flag for validation

**Known Limitations:**
- Blazor circuit persistence (new in .NET 10) lacks mature documentation; pattern emerging
- Large form performance (200+ fields) less documented; established pattern from data grids
- Multi-circuit state sync approaches not yet standardized; multiple viable patterns

---

## Questions for Next Research Phase

**Phase 1 Deep Dives:**
1. How exactly does ICircuitHandler access JWT claims during OnConnectionUpAsync?
2. Should refresh token endpoint accept token in body, header, or cookie?
3. What's the circuit timeout threshold in IronMonkey AppHost (when is circuit discarded)?
4. Does ProtectedSessionStorage encrypt stored data? If so, what's the key derivation?

**Phase 2 Architecture:**
1. Should state invalidation use SignalR Hub or separate invalidation endpoint?
2. What's acceptable WebSocket message frequency for form interaction (10/sec? 50/sec?)?
3. How to implement form state persistence without localStorage (server-side option)?

**Operations & Monitoring:**
1. What metrics indicate multi-circuit per user scenario (CircuitCount/UserCount ratio threshold)?
2. How to detect cross-tenant data access in production logs?
3. What token refresh failure rate is acceptable (< 1%? < 5%)?

---

## File Locations

All research files are in: `/home/manoj/projects/sandbox/IronMonkey/.planning/research/`

1. `PITFALLS_BLAZOR_UI.md` — Comprehensive pitfall catalog
2. `SUMMARY_BLAZOR_UI.md` — Executive summary
3. `IMPLEMENTATION_PATTERNS_BLAZOR.md` — Code patterns
4. `BLAZOR_UI_RESEARCH_INDEX.md` — This file

---

## How to Update Research

**When findings change (new .NET version, GitHub issue resolution):**
1. Update relevant section in PITFALLS_BLAZOR_UI.md
2. Add note with date and reason for change
3. Update SUMMARY_BLAZOR_UI.md confidence levels if affected
4. Update IMPLEMENTATION_PATTERNS_BLAZOR.md code if pattern changes

**When new pitfalls are discovered during Phase 1:**
1. Add to PITFALLS_BLAZOR_UI.md in appropriate severity section
2. Document prevention strategy based on actual implementation experience
3. Add integration test case to prevent regression

---

## Final Recommendation

**Phase 1 should prioritize in this order:**
1. **Circuit Identity Validation** — Security-critical; must be foundation
2. **Token Refresh + 401 Handling** — Required for functional admin UX
3. **Tenant Validation in Endpoints** — Defensively ensures data isolation
4. **DbContext + Tailwind Configuration** — Foundation for everything else

**Expected Phase 1 Scope:** 4-6 weeks for solid auth foundation + 5-6 admin endpoint patterns

**Risk if deferred:** Deferring critical pitfalls → rewrites in Phase 2 when security issues discovered in testing
