# Configurable Opportunity Pipelines and Multiple Pipelines

## Context

Leads have a properly configured pipeline: `PipelineStage` is a tenant entity with ordering, stage types, a reorder endpoint with a real concurrency check, impact reporting before deletion, and `StageTransition` history. Opportunities have none of it. `Opportunity.Stage` is a bare `string`, `MarkAsLost()` assigns the literal `"Lost"`, and `UpdateStage(string)` accepts anything a caller passes. A tenant cannot rename, reorder or add opportunity stages, and no two industries agree on what those stages are.

There is also exactly one lead pipeline per tenant. A dealership running new-car sales alongside service bookings, or a university running undergraduate admissions alongside executive education, needs more than one — with different stages, different fields and different routing — inside the same tenant.

## Prompt

Give opportunities the same configurable stage model leads already have, and allow a tenant to run more than one named pipeline.

### Opportunity stages

- Replace the free-string `Opportunity.Stage` with a reference to a tenant-configured stage entity, reusing the existing stage concepts — ordering, stage type including won/lost terminals, and active/inactive — rather than inventing a second, divergent model.
- Migrate existing opportunity rows by mapping their current string values onto seeded stages, including the literal `"Lost"` that `MarkAsLost()` has been writing. Do not drop or orphan existing opportunities, and do not leave rows pointing at a stage that does not exist.
- Keep won/lost semantics explicit. Closing an opportunity must set a terminal stage type rather than matching on a stage name, so a tenant that renames "Lost" to "Declined" does not break close logic, reporting, or the dashboard's exclusion of terminal work.
- Carry over the protections the lead pipeline already enforces: reordering rewrites the whole sequence in one transaction and rejects a submitted set that differs from the stored set; a stage holding opportunities cannot be deleted without reassignment; the last active stage cannot be removed; names are unique per tenant case-insensitively.
- Record opportunity stage transitions the way `StageTransition` records lead transitions, so stage-duration and velocity reporting is possible.

### Multiple pipelines

- Introduce a named pipeline entity owning an ordered set of stages, scoped to a tenant and to a record type (lead or opportunity). Every existing stage must end up attached to a default pipeline created by the migration — no tenant may be left with stages belonging to no pipeline.
- Let a lead or opportunity belong to exactly one pipeline at a time, and define what moving a record between pipelines does to its stage, its transition history, and its routing. Moving a record must never silently place it in a stage from a different pipeline.
- Scope stage pickers, board columns, list filters, dashboard aggregates and reports to the selected pipeline, with an explicit way to see across all of them. A tenant with two pipelines must never see one pipeline's counts presented as the tenant total.
- Allow custom fields, workflow rules and routing to target a specific pipeline as well as the whole tenant. Be explicit about precedence when both apply, and about what happens to a pipeline-scoped rule when its pipeline is removed.
- Keep a single-pipeline tenant's experience unchanged. A tenant that never creates a second pipeline should not be asked to choose one anywhere in the UI.

### Recipes

- Extend `RecipeContentModel` so a recipe can define multiple pipelines with their own stages, and opportunity stages alongside the existing lead stages. Older recipes without these sections must still provision — treat their flat stage list as the default lead pipeline.

## Acceptance criteria

- A tenant can rename, reorder, add and deactivate opportunity stages, and close an opportunity as won or lost through a terminal stage type rather than a magic string.
- Existing opportunities survive the migration with a valid stage, including rows previously marked `"Lost"`.
- Reordering, deletion-with-reassignment, last-active-stage refusal and case-insensitive name uniqueness behave for opportunity stages exactly as they already do for lead stages.
- A tenant can create a second pipeline with different stages; records, boards, filters, dashboards and reports stay correctly scoped, and cross-pipeline totals are explicit rather than accidental.
- A single-pipeline tenant sees no new required choices.
- Provisioning from an existing stored recipe still succeeds, and a new multi-pipeline recipe seeds correctly.
- Tests cover the migration of legacy stage strings, terminal-type close logic, reorder concurrency, cross-pipeline isolation of aggregates, and recipe backward compatibility.

## Likely implementation surfaces

- `IronMonkey.Data/Entities/Opportunity.cs`, `PipelineStage.cs`, `StageTransition.cs`, `Lead.cs`
- A new pipeline entity and a tenant migration with data backfill
- `IronMonkey.ApiService/Features/Leads/PipelineStages/` and `IronMonkey.ApiService/Features/Opportunities/`
- `IronMonkey.ApiService/Features/Reports/` and the dashboard endpoints
- `IronMonkey.Web/Components/Pages/Admin/Opportunities/`, `.../Leads/LeadBoard.razor`, `.../Configuration/PipelineStagesTab.razor`
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs`
- Tests in `IronMonkey.Tests`

Reuse the lead pipeline's hard-won rules. A second, subtly different stage implementation is the failure mode to avoid here.
