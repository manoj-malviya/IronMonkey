# IronMonkey — Feature Prompt Backlog

One prompt per task. Each is self-contained: context, requirements, acceptance criteria, and the
likely implementation surfaces. Each also names the traps already discovered in this codebase, so
an implementer does not rediscover them.

The organizing goal is **one deployment that adapts to many industries** — a dealership, a
university, a clinic and a broker each running on the same code, configured rather than forked.

## Status

Prompts 01–05 are written; 03, 04 and 05 have shipped (configuration workspace, CRM dashboard,
workflow execution history). 06–15 were added from a gap analysis against the current codebase
plus market research on what a multi-industry CRM is expected to have in 2026.

| # | Prompt | Theme | Status |
|---|--------|-------|--------|
| 01 | Tenant onboarding experience | Onboarding | written |
| 02 | Team management and invitations | Tenant admin | written |
| 03 | Pipeline configuration workspace | Configurability | shipped |
| 04 | CRM dashboard and UI polish | Usability | shipped |
| 05 | Workflow execution logs | Observability | shipped |
| 06 | Tenant terminology and localization | **Adaptability** | written |
| 07 | Communications hub — email, SMS, WhatsApp | Core CRM gap | written |
| 08 | Configurable opportunity pipelines | **Adaptability** | written |
| 09 | Custom objects and relationships | **Adaptability** | written |
| 10 | Granular permissions and record visibility | Security | written |
| 11 | Privacy, consent and data retention | Compliance | written |
| 12 | Global search, saved views, report builder | **Adaptability** | written |
| 13 | Integration platform, public API, SSO | Enterprise | written |
| 14 | Products, quotes and deal economics | Core CRM gap | written |
| 15 | AI assistance and lead scoring | Differentiation | written |

## What the gap analysis found

The system is genuinely configurable in its **fields** — custom fields, pipeline stages, workflow
rules, routing and industry recipes all work, and the traps in them are documented in `CLAUDE.md`.
The gaps cluster in four places:

**Configurable data, fixed vocabulary.** Every screen says "Lead" and "Opportunity" regardless of
what the tenant calls them, there is no per-tenant currency, timezone or locale, and
`Tenant.ThemeSettings` is an unused nullable string. → 06

**Configurable leads, fixed everything else.** `Opportunity.Stage` is a bare string with a
hardcoded `"Lost"` while leads get a full stage model; there is one pipeline per tenant; a tenant
cannot define a record type of its own, so Vehicles, Programs and Policies get flattened into
custom fields on a Lead. → 08, 09

**Missing CRM table stakes.** There is no working outbound messaging — both email classes are dead
code from another product, one of which reports success it never confirmed — no product catalog or
quoting, and no search service at all. → 07, 12, 14

**Missing enterprise and regulatory floor.** Permissions are tenant-wide with no record-level
visibility despite `AssignedToUserId` existing; there is no consent, retention or erasure
mechanism, and soft delete means "delete" keeps personal data forever; there is no public API,
event subscription or SSO. → 10, 11, 13

## Suggested order

Dependencies run roughly left to right:

```
06 terminology ─┐
08 pipelines   ─┼─→ 09 custom objects ─→ 12 search/reports ─→ 15 AI
10 visibility  ─┘                           ▲
07 communications ─→ 11 privacy ────────────┘
13 integrations
14 products/quotes
```

- **06, 08, 10** are foundational and largely independent — do them first.
- **07** unblocks **11** (consent is enforced at send time) and **15** (draft messages).
- **09** is the largest lever on multi-industry fit and should precede **12**, since the report
  builder needs to reach tenant-defined objects to be worth building.
- **10** should precede **12** and **15**: search, reports, exports and AI summaries are the four
  easiest ways to disclose records a viewer should not see.
- **15** is explicitly gated — build it last, and only if scores are explainable.

## Cross-cutting rules every prompt assumes

- Tenant isolation is the primary boundary. Raw SQL over the JSONB bags bypasses the global query
  filters and must filter `TenantId` explicitly; use `jsonb_exists(col, @key)`, never `?`.
- Recipes are platform catalog data. Extending `RecipeContentModel` must keep older stored recipes
  provisioning, and a tenant editing seeded data must never write back to the catalog.
- `admin:access` and `SuperAdmin` stay platform-only, enforced server-side and pinned by test.
- Custom field values are keyed by definition id; `FieldKey` is the integration handle.
- Nothing referenced is deleted silently — report the blast radius first, as stages and fields do.
- New date logic reuses `DashboardDateRange`'s half-open `[From, ToExclusive)` contract.
- Anything persisted that could quote a secret goes through `WorkflowDiagnosticRedactor`.
