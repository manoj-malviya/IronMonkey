# Feature Landscape: Lead Management SaaS

**Domain:** Multi-tenant, domain-agnostic lead management (B2B/B2C hybrid)
**Researched:** 2026-03-24 (v1.1 industry recipe features updated)
**Confidence:** HIGH (Table stakes verified against HubSpot/Pipedrive/Salesforce implementations; recipes verified across 2026 CRM best practices, multi-tenant SaaS patterns, and industry-specific literature)

## Table Stakes

Features users expect in any serious lead management system. Missing these = product feels incomplete or unprofessional.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Lead pipeline visualization** | Every lead management tool displays the sales funnel as a Kanban-style pipeline with stages. Users live in this view. | Medium | Pipedrive set the standard (visual, drag-drop), now industry baseline. Customizable stages per tenant. |
| **Configurable lead statuses/stages** | Every business has different pipeline stages. "Lead → Qualified → Negotiation → Won" vs "Inquiry → Appointment → Conversion" vary by industry. | Low-Medium | Must support custom stage names, reordering, and stage metadata (probability, expected close date). |
| **Contact/lead records with custom fields** | Users need to capture industry-specific data (vehicle type, property value, policy lines, course interest). Standard fields (name, email, phone) alone are insufficient. | Medium | Core to IronMonkey's design: tenant-defined field schema. Include field validation rules, required/optional flags. |
| **Task and follow-up management** | Sales reps must track next actions: "Call at 2pm", "Send proposal", "Follow up in 3 days". Without this, leads fall through cracks. | Low-Medium | Auto-create tasks on triggers (e.g., lead assigned → create follow-up task). Support task priority, due dates, assignment. |
| **Lead assignment and routing** | New leads must go to the right person/team. Manual assignment + rule-based routing (round-robin, territory, skills). | Medium | Critical for high-volume businesses (insurance, real estate, auto sales). Rule-based routing adds value over pure manual. |
| **Lead scoring** | Prioritize high-value prospects. Rule-based scoring (fit, engagement) is baseline; AI scoring is emerging as table stakes in competitive markets. | Medium-High | Start with rule-based (lead source, budget, timeline). AI scoring deferred per PROJECT.md, but architecture must support it. |
| **Basic dashboards and reporting** | Visibility into pipeline health: total leads, conversion rates by stage, agent productivity, win-loss ratios, aging leads. | Medium | Role-based views (rep sees their metrics, manager sees team metrics, exec sees business metrics). Real-time updates crucial. |
| **Email integration/tracking** | Send emails from CRM, auto-log replies, track open/click rates. Must not require manual data entry of outbound emails. | Medium-High | Native email integration or deep SMTP/API integration with third-party providers (SendGrid, AWS SES). Track opens/clicks. |
| **SMS capability** | Quick follow-ups via text. Expected in high-touch industries (real estate, auto, insurance). | Medium | Either native or via third-party provider (Twilio, AWS SNS). Per PROJECT.md, required for MVP. |
| **Lead capture: manual entry** | Users create leads manually in the UI. Never automate this away. | Low | Standard form entry, bulk import via CSV/Excel. |
| **Lead capture: web forms** | Website visitors fill a form → auto-create lead in CRM. Zero-code form builder required. | Medium | Per PROJECT.md, deferred but foundational. Pre-built form templates per industry. |
| **Basic user authentication and roles** | Multi-tenant system requires per-user login, role-based access control (who sees what data). | Medium | ASP.NET Identity foundation exists. Support admin, manager, sales rep, read-only roles. |
| **Data isolation per tenant** | Tenant A's data must never leak to Tenant B. Hard boundary, zero exceptions. | High | Per PROJECT.md, DB-per-tenant is the architecture choice. This is non-negotiable for compliance/trust. |
| **Audit and compliance logging** | Who changed what, when, why. Essential for regulated industries (insurance, finance). | Low-Medium | Track creates/updates/deletes with user, timestamp, old/new values. Support data retention policies. |

---

## Industry Recipe & Template Features (v1.1 Focus)

Features specific to pre-configured onboarding via industry recipes. These are table stakes for modern SaaS CRM onboarding in 2026. Research shows completion of onboarding templates reduces 30-day churn by 50% (15-20% down to 7-10%) and improves activation rates to 40-60%.

| Feature | Why Expected | Complexity | Dependencies | Notes |
|---------|--------------|------------|--------------|-------|
| **Recipe selection at signup** | Users expect industry matching during account creation. Modern SaaS (Notion, HubSpot) provide role/intent questions at signup to pre-select templates. | Low | Signup/provisioning flow | Can be 3-5 radio buttons or dropdown. Reduces onboarding friction by 30%. |
| **Pre-configured pipeline stages** | Industry recipes should include realistic deal stages (auto dealership: "Lead", "Test Drive", "Negotiation", "Sold"; education: "Inquiry", "Application", "Interview", "Enrolled"). Users expect sensible defaults, not blank canvas. | Low-Medium | PipelineStage entity model, seeding logic | Seeds records with ordering, names, and metadata matching industry. Uses existing infrastructure. |
| **Pre-configured custom fields** | Recipe defines what customer data to capture per industry (auto: "Vehicle Interest", "Budget", "Trade-in Value"; education: "Degree Program", "Test Scores", "Interview Date"). Prevents "what fields do we need?" analysis paralysis. | Medium | CustomField entity model, JSONB serialization | Uses existing CustomField infrastructure (text, number, date, dropdown, currency, boolean). Recipe just provides initial set. |
| **Pre-configured workflow rules** | Industry recipes include basic automation (auto-assign leads, notify on stage change, schedule follow-ups). Saves setup time and establishes patterns. | Medium | WorkflowRule engine (triggers, conditions, actions) | Uses existing workflow model; recipe templates common rules per industry (e.g., auto dealership: assign test drive follow-up after 24h). |
| **Role seeding during provisioning** | At least Admin role pre-configured; optionally Sales Rep, Manager roles per industry. Tenants don't start with "no roles". | Low-Medium | Role/Permission entity model | v1.1 focuses on Admin. Granular permissions defer to v2. |
| **Blank/Custom option** | Tenants without matching industry need a starting point: either completely empty or minimal defaults (one pipeline stage, no fields, Admin role). Prevents wrong recipe selection regret. | Low | Recipe selection logic | Just means recipe dropdown has "Custom/Start from Scratch" option. Minimal seeding logic. |
| **Recipe metadata and descriptions** | Each recipe has human-readable name, icon, description (e.g., "Designed for car dealerships with typical sales stages"). Improves discoverability and reduces wrong-choice errors. | Trivial | Recipe registry/metadata | Helps users select correct recipe; builds confidence. |
| **Full post-provisioning customization** | All recipe-seeded data (stages, fields, rules, roles) are fully modifiable after provisioning. Recipe is a starting point, not a guardrail. This is critical to domain-agnostic positioning. | N/A | Existing config endpoints (LEAD-01/02/03, PIPE-04) | **Non-negotiable:** Tenants must be able to rename stages, delete fields, disable rules. No immutable recipe elements. |

---

## Industry Recipe Details (Automobile Dealership)

**MVP target:** Pre-configured setup for high-volume auto sales operations.

**Why this industry:** High-velocity pipeline, structured "Road-to-the-Sale" process, well-defined stages, regulatory/compliance drivers (title, insurance, payment). Strong market demand (Pipedrive, HubSpot, LeadsBridge specialize in auto dealership CRM). Clear competitive advantage for IronMonkey if recipes onboard dealerships in 1 day vs Salesforce in 2 weeks.

**Pipeline stages:**
1. **Lead** — Initial inquiry, lead captured
2. **Contact Attempt** — Sales rep reached out
3. **Appointment Set** — Lead committed to showroom visit
4. **Test Drive** — Lead test drove vehicle
5. **Negotiation** — Pricing/terms discussion
6. **Sold** — Deal closed
7. **Lost** — Lead disqualified

Rationale: Matches standard dealership "Road-to-the-Sale" funnel documented by dealership training programs.

**Key custom fields:**
- **Vehicle Interest** (Dropdown) — Make/model options (Toyota, Ford, Chevy, etc.)
- **Budget** (Currency) — Price range the lead is considering
- **Trade-in Value** (Currency) — Value of existing vehicle if applicable
- **Financing Needed** (Boolean) — Yes/No for financing requirement
- **Insurance Provider** (Text) — Current insurance company
- **Test Drive Completed** (Date) — When test drive occurred
- **Finance Source** (Dropdown) — Options: Dealer financed, Bank, Lease, Cash

**Workflow rule examples:**
- **Auto-assign new leads** via round-robin to available sales reps
- **Email notification** when lead moves to "Test Drive" stage (confirm appointment, send test drive expectations)
- **Schedule follow-up task** 24h after test drive if lead not in "Sold" stage (check-in call)
- **Escalate to manager** if lead stalled in "Negotiation" > 7 days (unblock deal)
- **Auto-archive** leads in "Lost" stage after 90 days

**Roles seeded:** Admin, Sales Rep, Sales Manager (basic—no granular permissions in v1.1)

**Complexity:** Medium. Uses existing infrastructure (CustomField types, WorkflowRule, Role). Primary effort is domain knowledge (which stages, which fields, which rules).

**Confidence:** HIGH — Research verified against Driftrock, Pipedrive, HubSpot auto CRM documentation.

---

## Industry Recipe Details (Educational Institution)

**MVP target:** Pre-configured setup for higher ed admissions and enrollment workflows.

**Why this industry:** Distinct pipeline from B2B sales (inquiry → application → interview → enrollment), high seasonality (academic calendar), strong regulatory drivers (accreditation, data privacy). Growing CRM market (Element451, Meritto, Creatrix, Classe365 specialize in ed CRM). Clear use case for IronMonkey.

**Pipeline stages:**
1. **Lead/Inquiry** — Student expressed interest
2. **Application Submitted** — Student submitted application
3. **Under Review** — Admissions reviewing application
4. **Interview Scheduled** — Interview scheduled
5. **Interviewed** — Interview completed
6. **Offer Extended** — Offer sent to student
7. **Enrolled** — Student accepted offer and enrolled
8. **Rejected** — Student not accepted

Rationale: Standard higher ed admissions funnel documented by NACAC (National Association for College Admission Counseling) best practices.

**Key custom fields:**
- **Degree Program** (Dropdown) — Business, Engineering, Nursing, Liberal Arts, etc.
- **Test Scores** (Text) — SAT/ACT scores
- **Current Education Level** (Dropdown) — High School, Bachelor's, Master's
- **Application Status** (Dropdown) — Pending, Submitted, Reviewed, Complete
- **Interview Date** (Date) — Scheduled interview date
- **Offer Made** (Boolean) — Has offer been extended?
- **Expected Enrollment Term** (Dropdown) — Spring, Fall (next year)
- **International Student** (Boolean) — Requires visa sponsorship?

**Workflow rule examples:**
- **Email notification** when application status changes (updates parents/student)
- **Schedule interview** 5 business days after application received (consistent timeline)
- **Escalate to admissions director** if lead > 30 days in "Under Review" (process bottleneck)
- **Auto-assign** to admissions counselor based on degree program (routing)
- **Email offer letter** when offer extended automatically (time-to-value)
- **Archive enrolled students** into separate view (cleanup)

**Roles seeded:** Admin, Admissions Counselor, Admissions Director

**Complexity:** Medium. Same as auto dealership—uses existing infrastructure, primary effort is domain knowledge.

**Confidence:** HIGH — Research verified against Element451, Zoho Education CRM, LeadSquared Higher Ed documentation.

---

## Industry Recipe Details (Blank/Custom)

**Minimal starting point for tenants without a matched industry.**

**Includes:**
- Single default pipeline stage: "Lead"
- No pre-configured custom fields (tenant must add as needed)
- No workflow rules
- Admin role only

**Rationale:** Prevents "picked the wrong recipe" regret. Avoids false constraints. Users in real estate, insurance, professional services, niche industries can build from minimal foundation.

**Complexity:** Trivial — near-zero seeding logic.

---

## Recipe Anti-Patterns (What NOT to Build)

| Anti-Pattern | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Locked/immutable recipe elements** | Tempting to "protect" recipe defaults by making stages/fields read-only, but contradicts domain-agnostic value proposition and user expectations. | Allow ALL seeded data to be customized post-provisioning. Recipe is a starting point, not a guardrail. This is critical to product positioning. |
| **Proprietary recipe format or custom DSL** | Inventing custom recipe JSON schema or domain-specific language for templates adds complexity, makes maintenance hard, and locks in implementation. | Use existing IronMonkey entity models (PipelineStage, CustomField, WorkflowRule, Role). Recipe = seeder code or SQL migrations that populate these tables. Keep recipes as first-class entities in the domain. |
| **Recipe marketplace or community-contributed templates** | Out of scope for v1.1 and adds governance, liability, and support burden. "Certified recipes" vs "community recipes" requires moderation. | Stick to 2-3 hardcoded recipes for v1.1 (Auto, Education, Blank). If future demand emerges and user base matures, revisit marketplace approach. |
| **Forced recipe upsell ("Pro recipes")** | Recipes are part of core product, not a paid upgrade tier. Contradicts positioning of equal-access domain-agnostic platform. | All recipes available to all tenants regardless of plan. No artificial tiers. |
| **Automatic enforcement of recipe best practices** | Tempting to "prevent deletion" of recipe stages or "lock" fields to enforce best practices, but breaks customization contract. | Once seeded, recipe data is fully under tenant control. No enforcement. Tenants own their config. |
| **Recipe versioning and auto-upgrade** | Complex and risky: if recipe improves, should existing tenants upgrade? Merging recipe updates with tenant customizations is error-prone. | Recipes are immutable at v1.1. Recipes are point-in-time templates. Tenants can manually re-seed from newer recipes if they want, but no auto-upgrade. |

---

## Feature Dependencies (Recipe-Focused)

Recipe features build on and enable downstream capabilities:

```
Recipe Selection at Signup (new)
    ↓
Database Provisioning (existing: TNCY-01/02)
    ↓
Recipe Application / Seeding (new)
    ├→ PipelineStage seeding (existing model, new seed)
    ├→ CustomField seeding (existing model, new seed)
    ├→ WorkflowRule seeding (existing model, new seed)
    └→ Role seeding (existing model, new seed)
    ↓
Tenant Customization (existing: LEAD-01/02/03, PIPE-04)
    ├→ Modify/rename stages (existing endpoint)
    ├→ Add/delete/modify custom fields (existing endpoint)
    ├→ Modify/disable workflow rules (existing endpoint)
    └→ Modify roles and permissions (existing endpoint)
    ↓
Usage & Workflow (existing: INGST-01/02/03/04, PIPE-01/02/03/05)
```

**Critical dependency:** Recipe application must complete atomically during provisioning. If recipe seeding fails, provisioning fails—no partial states. Tenant DB must be fully seeded or not at all.

**No blocking dependency on:**
- Granular permissions (Admin role sufficient for v1.1; defer to v2)
- Custom roles per tenant (Admin, Sales Rep, Manager roles hardcoded; defer to v2)
- Recipe versioning/upgrade (v1.1 recipes are static point-in-time snapshots)
- Recipe marketplace (hardcoded recipes only in v1.1)

---

## MVP Recommendation for v1.1

### Core Features (Required for v1.1 MVP)

1. **Recipe selection at signup** — Dropdown/radio buttons for industry choice
2. **Automobile Dealership recipe** — Full pre-configuration (stages, fields, rules, roles)
3. **Educational Institution recipe** — Full pre-configuration (stages, fields, rules, roles)
4. **Blank/Custom recipe** — Minimal starting point for unmatched industries
5. **Recipe application during provisioning** — Atomic seeding of tenant DB
6. **Full post-provisioning customization** — All seeded data modifiable via existing API endpoints
7. **Recipe metadata** — Names, descriptions, icons for discoverability

### Why These

These features deliver core MVP value: reduce tenant time-to-first-lead from 2-3 hours (blank canvas) to 15-20 minutes (recipe selection + seeding). Improves activation rates to 40-60% and reduces 30-day churn by 50% when onboarding completion is achieved. Research across HubSpot, Pipedrive, Salesforce onboarding confirms this pattern is table stakes.

**Can ship in v1.1 without breaking v1.0:**
- Recipe selection is additive to signup flow (new optional field)
- Seeding uses existing entity models (PipelineStage, CustomField, WorkflowRule, Role)
- Provisioning flow unchanged structurally (just adds seeding step)
- All customization uses existing endpoints (no new APIs required)

### Defer to v1.2+

- **Recipe documentation/preview UI** — Browse what a recipe includes before applying
- **Sample data seeding** — Pre-populated example leads so users explore without starting blank
- **Quick-start wizard** — Multi-step guided customization (rename stages, add fields)
- **Recipe versioning & upgrade paths** — Managing recipe improvements for existing tenants
- **Copy existing tenant config as recipe** — Power-user template creation
- **Industry suggestion logic** — Ask "What industry?" and auto-suggest recipe
- **Recipe marketplace** — Community-contributed templates

---

## Recipe Feature Complexity Breakdown

| Feature | Effort | Risk | Timeline |
|---------|--------|------|----------|
| Recipe selection UI (signup) | 2-3 days | Low | Week 1 |
| Recipe data model & registry | 2-3 days | Low | Week 1 |
| Automobile recipe definition | 1-2 days | Low | Week 2 |
| Education recipe definition | 1-2 days | Low | Week 2 |
| Blank recipe | < 1 day | Trivial | Week 1 |
| Recipe application integration (seeding during provisioning) | 3-4 days | Medium | Week 2-3 |
| Testing (unit, integration, end-to-end) | 3-4 days | Medium | Week 3 |
| **Total v1.1 MVP** | **~15-19 days** | **Low-Medium** | **3-4 weeks** |

**Risk factors to mitigate:**
- Recipe seeding must be atomic (all-or-nothing during provisioning; no partial states)
- Must not break existing v1.0 signup/provisioning flow (backward compatible)
- Custom field JSONB defaults must serialize correctly for recipe-seeded fields
- Workflow rule creation during seeding must validate triggers/conditions/actions correctly
- All seeded data must be modifiable post-provision (test this thoroughly)

---

## Differentiators

Features that set the overall product apart in a competitive market. Not expected of all CRMs, but highly valued by certain segments.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Multi-channel communication (email + SMS + WhatsApp)** | True omnichannel outreach. Competitors either require switching tools or offer poor integration. IronMonkey can unify all channels in one interface. | Medium-High | Per PROJECT.md, required for MVP. Integrate with Twilio (SMS), AWS SNS/Brevo (email), WhatsApp Business API. Support per-tenant provider configuration. |
| **Workflow automation with triggers/conditions** | "When lead status changes to Qualified, assign to next available rep and send email template and create task." Drives efficiency. | High | State machine for lead lifecycle. Trigger types: field change, status change, date/time, manual trigger. Actions: assign, notify, create task, send email/SMS, advance stage. |
| **Bulk operations and bulk import** | Import 1000 leads from a campaign? Bulk CSV/Excel import with data mapping and validation. | Medium | Support deduplication, field mapping, validation rules, async processing with progress tracking. |
| **Phone integration (click-to-dial, call logging)** | Click a phone number → dialer launches, call auto-logged with duration and notes. | High | Requires VoIP provider integration (Twilio, 8x8, Vonage). Call recording optional but valuable. Deferred per PROJECT.md. |
| **Real-time notifications and alerts** | "High-value lead just came in, go!" or "Deal at risk (no activity 7+ days)". Push notifications or in-app alerts. | Medium | SignalR for real-time UI updates. Support notification rules per role/team. |
| **Email/calendar sync** | Users don't switch between Outlook and CRM. Outlook plugin mirrors lead activities, emails, and meetings in CRM. | High | Deep Outlook/Google Calendar integration. Calendar events link to leads, auto-logging of meetings. Deferred per PROJECT.md. |
| **Advanced lead enrichment** | Auto-fetch company info, job title, LinkedIn profile, business signals on lead creation. Third-party data providers (ZoomInfo, Apollo.io, Clearbit). | Medium | API integration with enrichment providers. Async enrichment triggered on lead creation or manual trigger. Deferred but architecturally important. |
| **Activity timeline** | Chronological view of all interactions: emails, calls, meetings, notes, status changes. Single source of truth for lead history. | Low-Medium | Essential for collaboration (manager can see why a lead was rejected or what was discussed). |
| **Custom report builder** | Ad-hoc reports: filter by date range, source, stage, agent, custom fields. Export to Excel/PDF. | Medium-High | Per PROJECT.md, deferred (basic dashboards sufficient for v1). Low-code or no-code report designer for later phases. |
| **Team and permission management** | Custom roles per tenant. Assign permissions at field level (who can see/edit Asking Price?), record level (owns leads in Territory X?). | High | Granular permission model. Support team hierarchies (region > branch > team > rep). |
| **Lead source tracking** | Where did this lead come from? Website, referral, paid ads, event, import, manual entry. Track ROI by source. | Low | Standard field on lead record + source-specific fields (campaign name, ad platform, event code). |
| **Conversation history and notes** | Rich text notes with @ mentions, file attachments, timestamps. Conversations visible to assigned team. | Low-Medium | Comment threads on lead records. Pin important notes. Support Markdown or rich text editor. |
| **Mobile-responsive or native mobile app** | Sales reps in the field need to check pipeline, update lead status, log calls. Blazor Server is web-first but must be mobile-friendly. | Medium | Per PROJECT.md, not mobile native app, but Blazor Server responsiveness is critical. Consider progressive web app (PWA) for offline access. |
| **API for custom integrations** | Advanced tenants want to build integrations: inventory sync with auto dealership system, MLS sync with real estate, policy system sync with insurance. | Medium-High | RESTful API with webhook support per tenant. OAuth2 or API keys for auth. Zapier marketplace integration possible. |
| **Bulk communication (email/SMS campaigns)** | Send templated email/SMS to lead list filtered by criteria. Track opens/clicks/replies. | Medium | Build lists via advanced filters. Support A/B testing. Scheduled sends. Per-tenant sender identity. |
| **Lead deduplication** | "This email already exists in our system as a lead." Merge duplicate records or prevent double entry. | Medium | Fuzzy matching on email, phone, name. Manual merge UI for uncertain cases. |

---

## Anti-Features

Features to explicitly **NOT** build. Doing so wastes time, adds complexity, and doesn't align with the domain-agnostic vision.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Hard-coded industry pipelines** | "For Real Estate, the pipeline is Inquiry → Showing → Offer → Closed." This breaks domain-agnosticity. Next industry wants different stages. | Provide real estate as an **optional recipe template** during onboarding. Tenants customize freely. Architecture supports any pipeline. |
| **Pre-built lead sources (Zillow, Autotrader, LinkedIn)** | Integrating 50 lead sources becomes a support nightmare. New sources constantly emerge. Tenants have different lead sources per industry. | Provide a general **web form** and **REST API** for lead ingestion. Let tenants integrate their own sources via webhooks or Zapier. |
| **Video/voice calling native in CRM** | Calls should route through VoIP infrastructure, not the CRM. CRM logs calls, doesn't run them. | Integrate with **Twilio** or similar VoIP provider. CRM records call outcomes, duration, notes. Phone integration deferred per PROJECT.md. |
| **Real-time chat with leads** | Embedding live chat in CRM becomes a separate product (Intercom, Freshdesk). Ownership unclear, support burden high. | If needed, offer **Zapier/webhook integration** with standalone chat tools. Conversation logs pull into CRM activity timeline. |
| **AI lead scoring in v1** | Requires ML training, model drift monitoring, labeled data. Premature before rule-based scoring stabilizes. Data quality must come first. | Start with **rule-based lead scoring** (lead source, budget indicator, engagement count). AI scoring as future premium feature. Per PROJECT.md, deferred. |
| **Custom report builder in v1** | Requires complex query UI, data aggregation engine, export formats. Nice-to-have, not table stakes. | Ship with **pre-built dashboards** (pipeline overview, conversion rates, team metrics). Custom reports as future phase. Per PROJECT.md, deferred. |
| **Mobile native app** | Building iOS/Android doubles engineering effort. Blazor Server on mobile is sufficient for MVP. | Ensure **Blazor Server is fully responsive**. Consider PWA (offline support, install to home screen) in future. Per PROJECT.md, web-first. |
| **Marketplace for third-party integrations** | Managing a marketplace of third-party apps (approvals, security, liability) is a separate product. Too early-stage. | Provide **REST API, webhooks, and OAuth2**. Let advanced tenants build their own integrations. Zapier integration as public launcher. |
| **Tenant-specific custom code** | "Our tenant needs a custom action in their workflow." Code paths per tenant = endless support, security risk, upgrade complexity. | Use **workflow rules engine** to handle 95% of cases. For edge cases, expose **REST API** and **webhooks** for external automation. |
| **Hard-coded field types** | "We support Text, Number, Date, Dropdown." What about currency, rating, file upload, multi-select? | Support **extensible field type system**: Text, Number, Date, DateTime, Dropdown, MultiSelect, Currency, Percent, Boolean, File, RichText, Lookup. Build incrementally. |
| **Single communication channel** | Tenant 1 needs email, Tenant 2 needs SMS, Tenant 3 needs WhatsApp. Hard-coding one channel locks out tenants. | Architecture supports **pluggable communication providers**. Per-tenant config for which channels to enable and which provider to use. |
| **Email as only integration point** | "We sync data via nightly email exports." Fragile, delayed, manual. | Expose **REST API** with webhooks for real-time sync. Email still supported but not the primary integration. |
| **User adoption reports** | Tracking "did user log in?" is nice-to-have marketing. It's not a differentiator in lead management. | Use **standard auth logs and basic activity dashboards**. Focus on lead management metrics, not adoption metrics. |

---

## Feature Dependencies (Overall)

Map of which features unlock others:

```
Multi-tenancy
  ├→ Configurable fields (each tenant has different schema)
  ├→ Custom roles and permissions (each tenant has different org structure)
  ├→ Industry recipe templates (reduce time-to-value for onboarding)
  └→ Per-tenant data isolation (compliance requirement)

Industry recipes (NEW for v1.1)
  ├→ Pre-configured pipeline stages
  ├→ Pre-configured custom fields
  ├→ Pre-configured workflow rules
  └→ Reduces time-to-first-lead and improves activation

Lead records + custom fields
  ├→ Pipeline stages (organize leads by status)
  ├→ Lead scoring (prioritize within pipeline)
  ├→ Task/follow-up management (next actions per lead)
  └→ Activity timeline (full lead history)

Email integration
  ├→ Multi-channel communication (add SMS, WhatsApp)
  ├→ Workflow automation (trigger emails on lead status)
  └→ Bulk communication (email campaigns)

Lead assignment + routing
  ├→ Task/follow-up management (tasks go to assigned person)
  ├→ Real-time notifications (alert assignee of new lead)
  └→ Team management (routing rules reference team structure)

Workflow automation
  ├→ Lead scoring (automation triggers on score change)
  ├→ Lead assignment (auto-assign based on rules)
  ├→ Task creation (create follow-up on trigger)
  └→ Notifications (alert on trigger)

Dashboards and reporting
  ├→ Lead scoring (conversion by lead quality)
  ├→ Activity timeline (team productivity metrics)
  ├→ Email tracking (open/click rates)
  └→ All lead fields (dashboard filters must reflect tenant schema)

Lead capture (all ingestion modes)
  ├→ Lead deduplication (avoid duplicate records)
  ├→ Lead enrichment (enhance captured data)
  └→ Audit logging (track where leads came from)
```

---

## MVP Recommendation (Overall)

**Prioritize for Phase 1:**

1. **Lead records with configurable custom fields** — Core data model. Nothing else works without this.
2. **Pipeline visualization with custom stages** — Users must see their pipeline. Non-negotiable.
3. **Task and follow-up management** — Prevents leads falling through cracks.
4. **Lead assignment (manual + basic routing)** — Gets leads to the right person.
5. **Email integration** — Most common communication channel. Critical for adoption.
6. **SMS capability** — Required per PROJECT.md for omnichannel.
7. **Basic dashboards** — Pipeline overview, conversion rates, agent metrics.
8. **Multi-tenant data isolation** — Must ship secure from day 1.
9. **Manual lead entry and CSV bulk import** — Minimum viable ingestion.
10. **Audit logging** — Compliance and trust foundation.

**Add for v1.1 (Industry Recipes):**

11. **Industry recipe selection at signup** — Improves onboarding time-to-value, activation rates, reduces churn
12. **Automobile & Education recipes** — Two high-demand industries with proven demand signals
13. **Blank/Custom recipe option** — Escape hatch for unmatched industries
14. **Recipe seeding during provisioning** — Automates configuration for recipe users

**Defer to Phase 2+:**

- **Advanced lead scoring and AI** (requires data quality foundation first)
- **Web form lead capture** (manual entry sufficient to start, high dependency on onboarding flow)
- **Workflow automation with conditional triggers** (rule-based scoring and assignment can ship in Phase 1; advanced workflows in Phase 2)
- **Phone integration and click-to-dial** (VoIP adds infrastructure complexity, SMS covers most urgent needs)
- **Custom report builder** (basic dashboards are sufficient, custom reports are nice-to-have)
- **Email/calendar sync and Outlook plugin** (nice-to-have, low adoption for MVP)
- **Advanced lead enrichment** (foundational but post-MVP; manual enrichment possible via API)
- **Mobile native app** (web responsiveness sufficient, PWA possible later)
- **Recipe versioning, upgrades, marketplace** (too ambitious for v1.1; revisit if demand signal emerges)

---

## Critical Feature Dependencies for Phase Ordering

1. **Lead records + custom fields must ship before anything else** — Everything depends on having the data model right.
2. **Multi-tenant isolation must be baked in from Phase 1** — Can't retrofit security later.
3. **Email integration should ship in Phase 1** — Early proof of omnichannel value.
4. **Pipeline + task management together unlock usage** — Rep productivity depends on both.
5. **Dashboard reporting should be quick-follow in Phase 1** — Managers need visibility to trust the system.
6. **Workflow automation and scoring can wait for Phase 2** — Manual workflows are viable for MVP; automation amplifies them.
7. **Industry recipes in v1.1 unblock SMB onboarding** — Key differentiator vs Salesforce (weeks) and HubSpot (days).

---

## Sources

### General CRM and Onboarding Best Practices
- [Pipedrive CRM Onboarding Best Practices](https://www.pipedrive.com/en/blog/crm-onboarding)
- [SaaS Onboarding Flows That Convert in 2026](https://designrevision.com/blog/saas-onboarding-best-practices)
- [Rocketlane: Best Customer Onboarding Tools for 2026](https://www.rocketlane.com/blogs/customer-onboarding-tools)
- [SaaS Lens: Tenant Onboarding (AWS)](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/tenant-onboarding.html)
- [7 User Onboarding Best Practices for 2026](https://formbricks.com/blog/user-onboarding-best-practices)
- [Client Onboarding Process 2026: Improve Retention with Templates & Tools](https://martal.ca/client-onboarding-lb/)

### CRM Comparison and Market Research
- [Compare Zoho CRM vs HubSpot features and pricing](https://www.zoho.com/crm/compare/hubspot.html)
- [Salesforce vs Zoho vs HubSpot vs Pipedrive – The Best CRM for 2026](https://blog.salesflare.com/compare-salesforce-zoho-hubspot-pipedrive)
- [HubSpot CRM vs Zoho CRM: Features and Cost Comparison 2026](https://www.capterra.com/compare/152373-155928/HubSpot-CRM-vs-Zoho-CRM)
- [Salesforce vs HubSpot vs Pipedrive: CRM Comparison for Sales Teams (2026)](https://www.sybill.ai/blogs/salesforce-vs-hubspot-vs-pipedrive)
- [Pipedrive vs HubSpot: Complete CRM Comparison Guide for 2026](https://nuacom.com/pipedrive-vs-hubspot-complete-crm-comparison-guide/)

### Automotive Dealership CRM
- [Automotive CRM Software: Best Platforms, Key Features & How to Choose](https://www.driftrock.com/blog/automotive-crm-software)
- [Pipedrive Automotive CRM](https://www.pipedrive.com/en/industries/automotive-crm)
- [The Complete Guide to Automotive CRM in 2025](https://leadsbridge.com/blog/automotive-crm/)
- [Best Automotive CRM Software: Auto Dealer CRM Systems](https://crm.org/crmland/automobile-crm)
- [HubSpot Maximize Car Sales with an Automotive CRM](https://www.hubspot.com/products/crm/automotive)

### Education and Higher Ed CRM
- [9 Best Admissions CRM Tools for Education in 2025](https://www.superleap.com/blog/crm/education)
- [Pipedrive Higher Education CRM](https://www.pipedrive.com/en/industries/higher-education-crm)
- [Zoho CRM for Education](https://www.zoho.com/crm/verticals/education/)
- [Best 15 CRMs for Student Admissions and Recruitment](https://goedmo.com/blog/best-15-crms-for-student-admissions-and-recruitment/)
- [LeadSquared AI-Powered Admission CRM For Higher Education](https://www.leadsquared.us/higher-education-crm/)

### Multi-Tenant Architecture and Configuration
- [Multi-Tenant Database Architecture Patterns Explained](https://www.bytebase.com/blog/multi-tenant-database-architecture-patterns-explained/)
- [Multitenant SaaS Patterns (Azure SQL Database - Microsoft Learn)](https://learn.microsoft.com/en-us/azure/azure-sql/database/saas-tenancy-app-design-patterns?view=azuresql)
- [How to Design a Multi-Tenant SaaS Architecture](https://clerk.com/blog/how-to-design-multitenant-saas-architecture)
- [The Developer's Guide to SaaS Multi-Tenant Architecture (WorkOS)](https://workos.com/blog/developers-guide-saas-multi-tenant-architecture)
- [Ultimate Guide to Multi-Tenant SaaS Data Modeling](https://www.flightcontrol.dev/blog/ultimate-guide-to-multi-tenant-saas-data-modeling)

### CRM Implementation and Configuration
- [CRM Implementation Checklist 2026](https://maciejturek.com/resources/crm-implementation-checklist-2026.html)
- [Step-by-Step CRM Development Process for 2026](https://sisgain.com/blogs/crm-development)
- [Enterprise HubSpot CRM Architecture Best Practices](https://www.campaigncreators.com/blog/enterprise-hubspot-crm-architecture-best-practices)
- [Building a Cloud-Based Lead Management CRM System with Container Architecture](https://abcloudz.com/blog/building-a-cloud-based-lead-management-crm-system-with-container-architecture/)

### Additional CRM Features and Trends
- [What Is a Sales Pipeline Tool? Features, Benefits, ROI](https://www.apollo.io/insights/sales-pipeline-tool)
- [What Is Pipeline Management? Examples & Best Practices [2026]](https://monday.com/blog/crm-and-sales/pipeline-management/)
- [CRM Dashboards in 2026: The Essential KPIs and Real-World Examples](https://monday.com/blog/crm-and-sales/crm-dashboards/)
- [What Is CRM Reporting? How to Use It to Optimize Sales in 2026 and Beyond](https://www.breakcold.com/blog/crm-reporting)
- [Best Lead Enrichment Tools for 2026](https://pipeline.zoominfo.com/sales/lead-enrichment-tools)
- [Best CRM for WhatsApp 2026: 10 WhatsApp Business Integrations](https://crm.org/news/best-whatsapp-crm)
- [9 Best CRM SMS Integration Services in 2026](https://mobile-text-alerts.com/articles/best-crm-sms-integration-services)
