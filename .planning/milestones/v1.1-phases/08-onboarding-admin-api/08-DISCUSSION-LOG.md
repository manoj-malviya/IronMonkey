# Phase 8: Onboarding & Admin API - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md -- this log preserves the alternatives considered.

**Date:** 2026-03-26
**Phase:** 08-onboarding-admin-api
**Areas discussed:** Signup-to-recipe flow, Recipe API design, Authorization model, Error & edge cases

---

## Signup-to-Recipe Flow

### Q1: When should recipe selection happen?

| Option | Description | Selected |
|--------|-------------|----------|
| At signup | User picks recipe during signup. RecipeId stored on SignupRequest. Cleanest UX. | ✓ |
| At provisioning | RecipeId passed to provision endpoint. Admin chooses recipe. | |
| Two-step: browse then signup | User browses recipes anonymously first, then starts signup with chosen RecipeId. | |

**User's choice:** At signup
**Notes:** None

### Q2: What should happen to IndustryType string field?

| Option | Description | Selected |
|--------|-------------|----------|
| Replace with RecipeId | Drop IndustryType, add RecipeId (Guid?). Null means Blank/Custom. | ✓ |
| Keep both | Keep IndustryType as informational, add RecipeId for provisioning. | |
| You decide | Claude picks based on codebase. | |

**User's choice:** Replace with RecipeId
**Notes:** None

### Q3: Required or optional recipe selection?

| Option | Description | Selected |
|--------|-------------|----------|
| Optional, default to Blank | RecipeId nullable. Null = Blank recipe provisioning. | ✓ |
| Required | Must explicitly select a recipe including Blank. | |

**User's choice:** Optional, default to Blank
**Notes:** None

### Q4: Provisioning timing?

| Option | Description | Selected |
|--------|-------------|----------|
| Stay separate | Keep current approve -> manually provision flow. | ✓ |
| Auto-provision on approval | Approval triggers provisioning via Hangfire. | |

**User's choice:** Stay separate
**Notes:** None

---

## Recipe API Design

### Q1: Endpoint organization?

| Option | Description | Selected |
|--------|-------------|----------|
| /api/recipes | All under one path. Auth differentiates read vs admin. RESTful. | ✓ |
| Split public/admin | Public under /api/recipes, admin under /admin/recipes. | |
| You decide | Claude picks based on existing patterns. | |

**User's choice:** /api/recipes
**Notes:** None

### Q2: List endpoint response shape?

| Option | Description | Selected |
|--------|-------------|----------|
| Metadata + counts | Name, description, icon, slug, isBlank, version + stage/field/rule/role counts. | ✓ |
| Metadata only | Just name, description, icon, slug. | |
| Full content | Everything including JSONB content. | |

**User's choice:** Metadata + counts
**Notes:** None

### Q3: Preview endpoint response?

| Option | Description | Selected |
|--------|-------------|----------|
| Full structured content | Deserialized stages[], fields[], rules[], roles[], sampleLeads[]. | ✓ |
| Sections summary | Stage names, field names, rule names, role names only. | |
| You decide | Claude picks. | |

**User's choice:** Full structured content
**Notes:** None

### Q4: Update style?

| Option | Description | Selected |
|--------|-------------|----------|
| Full replace (PUT) | Replace entire content + metadata. Version auto-increments. | ✓ |
| Partial (PATCH) | Update individual sections or metadata fields. | |

**User's choice:** Full replace (PUT)
**Notes:** None

---

## Authorization Model

### Q1: Recipe list/preview access?

| Option | Description | Selected |
|--------|-------------|----------|
| Anonymous | AllowAnonymous for GET endpoints. Needed for signup flow. | ✓ |
| Authenticated only | Require JWT for all recipe endpoints. | |

**User's choice:** Anonymous
**Notes:** None

### Q2: Recipe admin access?

| Option | Description | Selected |
|--------|-------------|----------|
| Platform admin only | SuperAdmin role required for create/update/deactivate. | ✓ |
| Any authenticated user | Any logged-in user can manage recipes. | |
| You decide | Claude picks based on auth patterns. | |

**User's choice:** Platform admin only
**Notes:** None

---

## Error & Edge Cases

### Q1: Provisioning with deactivated recipe?

| Option | Description | Selected |
|--------|-------------|----------|
| Block provisioning | Return validation error. Must reactivate or change RecipeId. | ✓ |
| Allow it | IsActive only affects list endpoint visibility. | |
| You decide | Claude picks safest approach. | |

**User's choice:** Block provisioning
**Notes:** None

### Q2: Unique IndustrySlug?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, unique constraint | Unique across active recipes. Already has index from Phase 6. | ✓ |
| No uniqueness | Allow duplicate slugs. | |

**User's choice:** Yes, unique constraint
**Notes:** None

### Q3: Recipe content validation?

| Option | Description | Selected |
|--------|-------------|----------|
| Structure validation | FluentValidation on request DTO. Valid stage types, field types, non-empty names. | ✓ |
| No content validation | Accept any valid JSON. Trust admin. | |
| You decide | Claude determines depth. | |

**User's choice:** Structure validation
**Notes:** None

---

## Claude's Discretion

- Exact FluentValidation rules depth for recipe content
- Request/Response DTO design details
- How to compute counts for list response
- Migration naming for SignupRequest field change
- Test structure for recipe API endpoints

## Deferred Ideas

- Recipe upgrade/migration for existing tenants (v1.2)
- Blazor frontend for recipe selection UI
- Auto-provisioning on approval
- Recipe marketplace
- Recipe export/import
