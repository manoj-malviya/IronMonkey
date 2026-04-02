# Phase 7: Recipe Content - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-26
**Phase:** 07-recipe-content
**Areas discussed:** Automobile Dealership recipe, Educational Institution recipe, Sample lead data, Recipe seeding method

---

## Automobile Dealership Recipe

### Pipeline Stages

| Option | Description | Selected |
|--------|-------------|----------|
| Road-to-the-Sale | 6 stages: Inquiry -> Test Drive -> Negotiation -> F&I -> Sold -> Lost | ✓ |
| Extended flow | 8 stages adding Qualified and Delivery | |
| Minimal flow | 4 generic stages | |

**User's choice:** Road-to-the-Sale (6 stages)

### Custom Fields

| Option | Description | Selected |
|--------|-------------|----------|
| Core vehicle fields | 6 fields: Make, Model, Year, Budget, Trade-In, Contact | ✓ |
| Extended vehicle fields | 10 fields adding VIN, Mileage, Color, Financing | |
| You decide | Claude picks | |

**User's choice:** Core vehicle fields (6 fields)

### Workflow Rules

| Option | Description | Selected |
|--------|-------------|----------|
| Follow-up focused | 2 rules: Notify on Negotiation, Flag Stale Inquiry (48h) | ✓ |
| Comprehensive rules | 4 rules adding round-robin and F&I notification | |
| You decide | Claude picks | |

**User's choice:** Follow-up focused (2 rules)

### Roles

| Option | Description | Selected |
|--------|-------------|----------|
| Standard dealership roles | 3 roles: Sales Manager, Sales Executive, BDC Agent | ✓ |
| Extended roles | 5 roles adding F&I Manager, Service Advisor | |
| You decide | Claude picks | |

**User's choice:** Standard dealership roles (3 roles)

---

## Educational Institution Recipe

### Pipeline Stages

| Option | Description | Selected |
|--------|-------------|----------|
| Admissions funnel | 6 stages: Inquiry -> Application -> Under Review -> Interview -> Enrolled -> Declined | ✓ |
| Extended admissions | 8 stages adding Waitlisted and Offer Sent | |
| You decide | Claude picks | |

**User's choice:** Admissions funnel (6 stages)

### Custom Fields

| Option | Description | Selected |
|--------|-------------|----------|
| Core student fields | 6 fields: Program, Grade, Previous School, Guardian Name, Guardian Phone, Scholarship | ✓ |
| Extended student fields | 9 fields adding GPA, Extracurriculars, Start Date | |
| You decide | Claude picks | |

**User's choice:** Core student fields (6 fields)

### Workflow Rules

| Option | Description | Selected |
|--------|-------------|----------|
| Notification focused | 2 rules: Notify on Review, Flag Stale Inquiry (7d) | ✓ |
| Comprehensive rules | 4 rules adding auto-acknowledge and counselor notification | |
| You decide | Claude picks | |

**User's choice:** Notification focused (2 rules)

### Roles

| Option | Description | Selected |
|--------|-------------|----------|
| Standard admissions roles | 3 roles: Admissions Director, Admissions Officer, Academic Counselor | ✓ |
| Extended roles | 5 roles adding Registrar, Financial Aid Officer | |
| You decide | Claude picks | |

**User's choice:** Standard admissions roles (3 roles)

---

## Sample Lead Data

### Lead Count

| Option | Description | Selected |
|--------|-------------|----------|
| 5 leads per recipe | Spread across active stages, not too cluttered | ✓ |
| 3 leads per recipe | Minimal | |
| 10 leads per recipe | More realistic-looking | |

**User's choice:** 5 leads per recipe

### Data Quality

| Option | Description | Selected |
|--------|-------------|----------|
| Realistic placeholder names | Real-sounding, diverse, @example.com emails | ✓ |
| Obvious test data | "Test Lead 1", clearly fake | |
| You decide | Claude picks | |

**User's choice:** Realistic placeholder names

---

## Recipe Seeding Method

### Deployment Strategy

| Option | Description | Selected |
|--------|-------------|----------|
| EF migration like Blank | Same InsertData pattern, fixed GUIDs, deterministic | ✓ |
| Application startup seed | Check on startup, insert if missing | |
| Separate seed command | CLI or admin endpoint trigger | |

**User's choice:** EF migration (consistent with Blank recipe in Phase 6)

### SampleLeads in RecipeContentModel

| Option | Description | Selected |
|--------|-------------|----------|
| Add SampleLeads section | New SampleLeadDefinition DTO, added to RecipeContentModel | ✓ |
| Separate seed data file | Keep RecipeContentModel focused on config | |
| You decide | Claude picks | |

**User's choice:** Add SampleLeads section to RecipeContentModel

---

## Claude's Discretion

- Exact sample lead names, emails, phone numbers
- Custom field values for each sample lead
- ConditionJson/ActionJson structure for workflow rules
- SampleLeadDefinition DTO details
- Migration naming and structure
- Test assertions

## Deferred Ideas

- Recipe selection UI — Phase 8
- Recipe preview — Phase 8
- Admin CRUD — Phase 8
- Recipe upgrades — v1.2
- Additional industry recipes — future
