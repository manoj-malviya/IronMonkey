# AI Assistance and Lead Scoring

## Context

`PROJECT.md` lists AI lead scoring as Out of Scope, deferred. That was the right call while the data model was still moving, and it is worth revisiting now for one specific reason: a configurable multi-industry CRM is exactly the case where a *fixed* scoring model fails and a *learned* one has something to offer. What predicts a won deal at a dealership — test drive booked, trade-in present, finance pre-approved — has nothing in common with what predicts an enrolled student. The platform cannot author a scoring rule per vertical; each tenant's own history is the only usable signal.

The prerequisites are largely in place. `StageTransition` records pipeline movement, `ActivityLog` records changes via the SaveChanges interceptor, custom fields hold each vertical's real predictors, and Hangfire with a `tenant` queue already runs scheduled and per-tenant background work.

Treat this prompt as gated: it is worth building only once the work it depends on has landed, and only if the resulting scores are explainable to the user who is asked to act on them.

## Prompt

Add an AI assistance layer that adapts to each tenant's own data, with explanations and honest failure modes.

### Lead scoring

- Score leads on likelihood to convert, learned per tenant from that tenant's own outcome history — stage transitions, activity, field values — rather than from a platform-wide model. A single cross-tenant model trained on pooled data leaks one tenant's commercial patterns into another's predictions and is the wrong default for a DB-per-tenant product.
- Do not score at all until a tenant has enough labelled history to justify it. State the threshold, show "insufficient data" plainly, and fall back to an explainable rules-based score in the meantime. A confident-looking number derived from nine leads is worse than no number.
- Show why a lead scored as it did — the factors that moved it — in terms of the tenant's own fields. A score a salesperson cannot interrogate is a score they will learn to ignore.
- Recompute on a schedule and on significant change, through Hangfire on the `tenant` queue, never in the request path.
- Never let scoring change business state on its own. It may inform routing, sorting and surfacing; it must not reassign, disqualify or close a record without a human or an explicit tenant-authored workflow rule.
- Track accuracy over time and expose it. A model that has drifted must be visibly untrustworthy rather than quietly wrong, and a tenant must be able to turn scoring off entirely.

### Assistive features

- Summarize a lead's history — activity, communications, stage movement — into something readable before a call. Ground every summary in the record's actual data, cite what it drew on, and never assert a fact the record does not contain.
- Suggest a next action from the tenant's own patterns, as a suggestion the user accepts or dismisses. Record which suggestions were accepted; a suggestion engine nobody accepts is worth knowing about.
- Offer draft message text for the communications layer, always as an editable draft. Nothing generated reaches a customer without a human sending it.
- Suggest duplicate matches alongside the existing `FuzzySharp` detection rather than replacing a deterministic mechanism that currently works.
- Where a large language model is involved, treat lead data as untrusted input to it: a lead's notes field can contain instructions aimed at the model. Never let generated output trigger an action directly, and never let it be interpolated into SQL, a webhook URL, or an email recipient.

### Operating constraints

- Make the provider pluggable with a no-op implementation for tests, following the pattern the communications layer establishes. No test may require a live model or a network call.
- Be explicit in configuration and documentation about whether tenant data leaves the deployment, which provider receives it, and what retention that provider applies. A tenant in a regulated vertical needs to answer this before enabling anything here, and the privacy work's records of processing must reflect it.
- Let each tenant opt in per feature, and default to off. Do not send data to a third party because a default said it was fine.
- Bound cost per tenant with quotas and make consumption visible. Per-tenant model inference is the one feature in this system whose cost scales with someone else's usage.
- Redact through the existing `WorkflowDiagnosticRedactor` before persisting any prompt, response or diagnostic. The same reasoning that keeps webhook tokens out of workflow history applies here.
- Apply record visibility to every AI surface. A summary that includes records the viewer cannot see is a disclosure with a friendly interface on it.

## Acceptance criteria

- A tenant with sufficient history sees per-lead scores learned from its own data, with per-lead factor explanations in its own field terminology.
- A tenant below the data threshold sees an honest "insufficient data" state and an explainable rules-based fallback, never a fabricated score.
- No tenant's model or data influences another tenant's scores, asserted by test.
- Scoring never mutates business state by itself; any action stays with a human or an explicit workflow rule.
- Model accuracy is tracked and visible, and a tenant can disable every AI feature.
- Summaries and suggestions cite the records they drew on and introduce no facts absent from the data.
- Instructions embedded in lead-supplied text cannot cause an action, a query, or a message to be sent — covered by a prompt-injection test.
- All AI features are opt-in and off by default, quota-bounded, visibility-scoped, and redacted before persistence.
- Tests run with a no-op provider, no credentials and no network access.

## Likely implementation surfaces

- A new AI/scoring feature folder in `IronMonkey.ApiService/Features`
- `IronMonkey.Data/Entities/StageTransition.cs`, `ActivityLog.cs`, `Lead.cs`, plus score and model-metadata entities and a tenant migration
- Hangfire jobs on the `tenant` queue
- `IronMonkey.ApiService/Features/Leads/Duplicates/` and the existing `FuzzySharp` detection
- The communications layer's draft path and `WorkflowDiagnosticRedactor`
- `IronMonkey.Web/Components/Pages/Admin/Leads/` and the dashboard
- Tests in `IronMonkey.Tests`

Build this last. It depends on the communications, visibility and privacy work, and its value depends entirely on whether a salesperson trusts what it says.
