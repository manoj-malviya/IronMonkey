# Feature Landscape: Lead Management SaaS

**Domain:** Multi-tenant, domain-agnostic lead management (B2B/B2C hybrid)
**Researched:** 2026-03-19
**Confidence:** MEDIUM (WebSearch verified against CRM comparison articles and product feature matrices)

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

## Differentiators

Features that set a product apart in a competitive market. Not expected, but highly valued by certain segments.

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
| **Industry recipe templates** | Pre-built field schemas, pipelines, workflows, roles for auto dealerships, real estate, insurance, professional services. Onboarding wizard uses these. | Medium | Requires domain expertise. Start with 3-5 industries per PROJECT.md. Tenants can customize after selection. |
| **Team and permission management** | Custom roles per tenant. Assign permissions at field level (who can see/edit Asking Price?), record level (owns leads in Territory X?). | High | Granular permission model. Support team hierarchies (region > branch > team > rep). |
| **Lead source tracking** | Where did this lead come from? Website, referral, paid ads, event, import, manual entry. Track ROI by source. | Low | Standard field on lead record + source-specific fields (campaign name, ad platform, event code). |
| **Conversation history and notes** | Rich text notes with @ mentions, file attachments, timestamps. Conversations visible to assigned team. | Low-Medium | Comment threads on lead records. Pin important notes. Support Markdown or rich text editor. |
| **Mobile-responsive or native mobile app** | Sales reps in the field need to check pipeline, update lead status, log calls. Blazor Server is web-first but must be mobile-friendly. | Medium | Per PROJECT.md, not mobile native app, but Blazor Server responsiveness is critical. Consider progressive web app (PWA) for offline access. |
| **API for custom integrations** | Advanced tenants want to build integrations: inventory sync with auto dealership system, MLS sync with real estate, policy system sync with insurance. | Medium-High | RESTful API with webhook support per tenant. OAuth2 or API keys for auth. Zapier marketplace integration possible. |
| **Bulk communication (email/SMS campaigns)** | Send templated email/SMS to lead list filtered by criteria. Track opens/clicks/replies. | Medium | Build lists via advanced filters. Support A/B testing. Scheduled sends. Per-tenant sender identity. |
| **Lead deduplication** | "This email already exists in our system as a lead." Merge duplicate records or prevent double entry. | Medium | Fuzzy matching on email, phone, name. Manual merge UI for uncertain cases. |

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

## Feature Dependencies

Map of which features unlock others:

```
Multi-tenancy
  ├→ Configurable fields (each tenant has different schema)
  ├→ Custom roles and permissions (each tenant has different org structure)
  ├→ Industry recipe templates (reduce time-to-value for onboarding)
  └→ Per-tenant data isolation (compliance requirement)

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

## MVP Recommendation

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

**Defer to Phase 2+:**

- **Advanced lead scoring and AI** (requires data quality foundation first)
- **Web form lead capture** (manual entry sufficient to start, high dependency on onboarding flow)
- **Workflow automation with conditional triggers** (rule-based scoring and assignment can ship in Phase 1; advanced workflows in Phase 2)
- **Phone integration and click-to-dial** (VoIP adds infrastructure complexity, SMS covers most urgent needs)
- **Custom report builder** (basic dashboards are sufficient, custom reports are nice-to-have)
- **Email/calendar sync and Outlook plugin** (nice-to-have, low adoption for MVP)
- **Advanced lead enrichment** (foundational but post-MVP; manual enrichment possible via API)
- **Mobile native app** (web responsiveness sufficient, PWA possible later)

## Critical Feature Dependencies for Phase Ordering

1. **Lead records + custom fields must ship before anything else** — Everything depends on having the data model right.
2. **Multi-tenant isolation must be baked in from Phase 1** — Can't retrofit security later.
3. **Email integration should ship in Phase 1** — Early proof of omnichannel value.
4. **Pipeline + task management together unlock usage** — Rep productivity depends on both.
5. **Dashboard reporting should be quick-follow in Phase 1** — Managers need visibility to trust the system.
6. **Workflow automation and scoring can wait for Phase 2** — Manual workflows are viable for MVP; automation amplifies them.

## Sources

- [Compare Zoho CRM vs HubSpot features and pricing | CRM Software](https://www.zoho.com/crm/compare/hubspot.html)
- [Salesforce vs Zoho vs HubSpot vs Pipedrive – The Best CRM for 2026](https://blog.salesflare.com/compare-salesforce-zoho-hubspot-pipedrive)
- [HubSpot CRM vs Zoho CRM: Features and Cost Comparison 2026 | Capterra](https://www.capterra.com/compare/152373-155928/HubSpot-CRM-vs-Zoho-CRM)
- [What Is a Sales Pipeline Tool? Features, Benefits, ROI | Apollo](https://www.apollo.io/insights/sales-pipeline-tool)
- [What Is Pipeline Management? Examples & Best Practices [2026]](https://monday.com/blog/crm-and-sales/pipeline-management/)
- [Pipedrive vs Freshsales: Features and Cost Comparison 2026 | Capterra](https://www.capterra.com/compare/132666-155563/Pipedrive-vs-Freshsales)
- [LeadSquared Sales + Mobile CRM Review 2026](https://research.com/software/reviews/leadsquared-sales-mobile-crm-review)
- [AI Lead Scoring: What Is It & How To Do It Right In 2026?](https://www.leadsquared.com/learn/sales/ai-lead-scoring/)
- [Best Lead Enrichment Tools for 2026](https://pipeline.zoominfo.com/sales/lead-enrichment-tools)
- [Best CRM for WhatsApp 2026: 10 WhatsApp Business Integrations](https://crm.org/news/best-whatsapp-crm)
- [9 Best CRM SMS Integration Services in 2026](https://mobile-text-alerts.com/articles/best-crm-sms-integration-services)
- [Guide to WhatsApp CRM system in 2026 - NetHunt CRM](https://nethunt.com/guide-to-whatsapp-crm-system-in-2026)
- [Zapier integration structure for a CRM app - Zapier](https://docs.zapier.com/platform/reference/crm-app)
- [The differences between Zapier, webhooks & API | Watermelon](https://watermelon.ai/blog/differences-integrations-zapier-webhooks-api)
- [CRM dashboards in 2026: the essential KPIs and real-world examples](https://monday.com/blog/crm-and-sales/crm-dashboards/)
- [What Is CRM Reporting? How to Use It to Optimize Sales in 2026 and Beyond](https://www.breakcold.com/blog/crm-reporting)
- [Best Lead Capture Software for 2026](https://pipeline.zoominfo.com/sales/lead-capture-software-tools)
- [Lead Capture | Pipedrive](https://www.pipedrive.com/en/products/sales/lead-capture)
- [10 CRM best practices for efficient customer management [2026]](https://www.bigin.com/articles/10-crm-best-practices-for-effective-customer-management-2026.html)
- [Is your CRM in 2026 an asset or a possible trap? | Finelis Sales](https://www.finelis.com/is-your-crm-in-2026-an-asset-or-a-possible-trap/)
