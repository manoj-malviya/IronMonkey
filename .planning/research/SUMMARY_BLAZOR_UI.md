# Research Summary: Adding Blazor Server Admin UI with Tailwind CSS to IronMonkey

**Project:** IronMonkey v1.2 (Admin UI Foundation)
**Researched:** 2026-03-27
**Research Type:** Domain Pitfalls for Blazor Server + Tailwind CSS integration
**Overall Confidence:** MEDIUM-HIGH

---

## Executive Summary

Adding a Blazor Server admin UI to an existing .NET Aspire multi-tenant API introduces three categories of risk:

1. **Security/Multi-Tenancy Boundaries** (Critical): Circuit reconnect can silently change user identity, DbContext pooling can leak data across tenants, JWT expiration + reconnect causes silent API failures. These are architectural issues that appear late in development if not addressed in Phase 1.

2. **Developer Experience** (Moderate): Tailwind CSS hot reload is broken by dotnet watch (requires two terminals), multiple circuits per user create stale state issues, and form performance degrades with 100+ custom fields. These are solved with documentation and sensible defaults.

3. **Integration Plumbing** (Minor): Missing HTTP Authorization header, incorrect Tailwind CSS content paths, tenant validation gaps in endpoints—all preventable with clear patterns and testing.

**Key Finding:** The existing IronMonkey architecture (JWT in claims, IUserContext for tenant resolution, global query filters) provides a solid foundation. The pitfalls are integration pitfalls: keeping tenant boundaries enforced when wiring Blazor circuits to the API, and managing Blazor's stateful nature in a multi-tenant context.

---

## Key Findings

### Stack
- **Frontend:** Blazor Server (.NET 10.0) + Tailwind CSS (standalone CLI, no Node.js)
- **Authentication:** JWT tokens (tenant_id claim) + ProtectedSessionStorage + HttpClient DelegatingHandler for auto-refresh
- **Context:** IUserContext service (existing) for tenant extraction; must be used in all admin endpoints
- **Circuit Management:** ICircuitHandler required to validate user identity on reconnect

### Architecture
- **Multi-Tenant Isolation:** Achieved via TenantDbContext (never AddDbContextPool), IUserContext (tenant resolver), global query filters
- **Token Management:** JWT in claims (server-side), refresh token in httpOnly cookie (secure), DelegatingHandler for auto-refresh on 401
- **Circuit Lifecycle:** One circuit per connection; identity validation on reconnect critical to prevent cross-user contamination
- **Form State:** Component state held in memory; persistence required for large/complex forms

### Critical Pitfalls
1. **Silent user identity change on circuit reconnect** → implement ICircuitHandler identity validation
2. **DbContext pooling breaks tenant isolation** → use AddDbContext (not pooling)
3. **JWT expiration + circuit reconnect = silent failures** → DelegatingHandler for 401 interception + token refresh

### Moderate Pitfalls
1. **Tailwind hot reload broken by dotnet watch** → document two-terminal setup
2. **Multiple circuits per user = stale state** → circuit handler limits circuits per user
3. **Large form performance** → Virtualize, batch loading, debounce validation

### Minor Pitfalls
1. **HttpClient missing JWT token in header** → BearerTokenHandler in DelegatingHandler
2. **Tenant validation missing in endpoints** → every endpoint must use IUserContext + filter by tenant
3. **Tailwind content paths miss .razor files** → verify tailwind.config.js glob patterns

---

## Implications for Roadmap

The research identifies that **Phase 1 (Blazor Auth Guards) must solve 80% of the critical pitfalls** in its foundation. Deferring these to later phases risks requiring architectural rewrites.

### Recommended Phase 1 Focus

**Goals for Phase 1:**
1. **Authentication Foundation** — Login page with JWT token generation, ProtectedSessionStorage, BearerTokenHandler
2. **Circuit Identity Validation** — ICircuitHandler validates user identity on reconnect; rejects cross-user reconnects
3. **Token Refresh** — DelegatingHandler intercepts 401 responses, refreshes token, retries request
4. **Tenant Context in API** — Every admin endpoint validates tenant from JWT claims using IUserContext
5. **DbContext Isolation** — Verify TenantDbContext registration (AddDbContext, not pooling) in integration tests
6. **Tailwind Integration** — Configure tailwind.config.js, MSBuild build order, document two-terminal dev setup

**Testing Requirements:**
- Integration tests for circuit reconnect with expired JWT
- Integration tests for cross-tenant API calls (must be rejected)
- Integration tests for multiple circuits per user (should log warning or reject)
- Unit tests for JWT extraction + validation in endpoints
- Performance tests for form with 100+ fields (establish baseline)

**Documentation Requirements:**
- CLAUDE.md updated with: (a) two-terminal Tailwind setup, (b) circuit identity validation pattern, (c) DelegatingHandler token refresh, (d) tenant validation checklist
- Architecture decision: DbContext never uses pooling in multi-tenant system

### Phase 2-3 (Component Patterns)

After Phase 1 foundation is solid:
1. **Large Form Patterns** — Virtualize, tabs, batch loading for 200+ field forms
2. **State Persistence** — Form state saved to localStorage; restored on reconnect
3. **Multi-Circuit Management** — Hub-based state invalidation or single-circuit SPA-style patterns
4. **Performance Optimization** — Render time profiling, WebSocket message frequency reduction

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| **Critical Pitfalls** | HIGH | Validated against recent (2026) GitHub issues (#65272 circuit reconnect, #64607 circuit state), official Microsoft docs |
| **Token Management** | HIGH | Multiple sources confirm DelegatingHandler + refresh token pattern; JWT in claims is standard |
| **Multi-Tenant Isolation** | HIGH | IronMonkey already implements IUserContext; pitfalls are integration-specific, not foundational |
| **Tailwind Integration** | MEDIUM | Standalone CLI approach is recent (v4); hot reload issue confirmed across multiple sources but workarounds documented |
| **Blazor Performance** | MEDIUM-HIGH | Official docs recommend Virtualize + immutable parameters; large form scenario not extensively documented but pattern is proven |
| **DbContext Pooling** | HIGH | EF Core docs explicitly warn against pooling in multi-tenant systems; IronMonkey already uses AddDbContext |

**Overall:** Research is grounded in official documentation (Microsoft Learn), active GitHub issues from 2026, and production patterns from multi-tenant SaaS companies. Confidence is reduced only where emerging tech (Blazor persistence, circuit state) lacks mature documentation.

---

## Gaps to Address

**Phase 1 Deep Dives Needed:**
- [ ] Exact ICircuitHandler implementation for IronMonkey: How to access JWT claims in OnCircuitOpenedAsync?
- [ ] Token refresh endpoint design: Should /api/auth/refresh accept refresh token or cookie?
- [ ] ProtectedSessionStorage capacity: Can it reliably store JWT + refresh token?
- [ ] Circuit timeout thresholds: How long before circuit is discarded (IronMonkey default)?

**Phase 2+ Research Needed:**
- [ ] Hub-based state invalidation design for multi-circuit scenarios
- [ ] Form state persistence: localStorage + IndexedDB hybrid strategy for large forms
- [ ] Performance baseline: What's acceptable WebSocket message frequency for admin UI?
- [ ] Circuit memory profiling: Baseline memory per circuit for cost analysis

**Risk Monitoring:**
- [ ] Design system for form validation (100+ fields) before component implementation
- [ ] Establish performance benchmarks (render time, WebSocket latency) during Phase 1
- [ ] Set up circuit/user ratio monitoring before production deployment

---

## Roadmap Alignment

**Why Phase 1 must address critical pitfalls:**
1. **Circuit Identity Validation** — If not built into auth foundation, silent data leaks become possible in Phase 2+
2. **Token Refresh + 401 Handling** — Required to make admin UI functional beyond 15-30 min sessions
3. **Tenant Validation in Endpoints** — Must become a pattern/checklist before scaling to 20+ admin endpoints
4. **DbContext Registration** — Single decision point; changing this later requires revalidating all endpoint tests

**Why Phase 2 can defer:**
- Large form optimization (Virtualize patterns) — works with simple forms in Phase 1
- Multi-circuit state sync — rare in Phase 1 (admin focus); becomes important at scale
- Form state persistence — phase 1 forms simple; complex forms deferred to Phase 2

---

## Files Created

1. **PITFALLS_BLAZOR_UI.md** — Complete catalog of pitfalls with prevention strategies
   - 4 critical pitfalls (circuit identity, DbContext pooling, token expiration, missing Authorization header)
   - 7 moderate pitfalls (hot reload, multiple circuits, form performance, etc.)
   - Phase-specific warnings and prevention checklist

2. **SUMMARY_BLAZOR_UI.md** (this file) — Executive summary for roadmap planning

---

## Next Steps

1. **Validate Phase 1 scope** against critical pitfall prevention checklist
2. **Design ICircuitHandler pattern** for IronMonkey (how to validate identity?)
3. **Implement BearerTokenHandler** for HttpClient token refresh
4. **Create CLAUDE.md updates** documenting Tailwind dev setup + auth patterns
5. **Build integration test framework** for circuit reconnect, token expiration, tenant isolation
6. **Establish performance baselines** for form rendering with 100+ fields
