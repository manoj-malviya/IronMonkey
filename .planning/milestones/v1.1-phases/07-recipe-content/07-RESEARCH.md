# Phase 7: Recipe Content - Research

**Researched:** 2026-03-26
**Domain:** Domain recipe seeding (Automobile and Educational Institution), custom field definitions, workflow rules, sample lead population
**Confidence:** HIGH

## Summary

Phase 7 builds on the recipe infrastructure from Phase 6 to define and seed two industry-specific recipes with complete domain content — pipeline stages, custom fields, workflow rules, and roles — plus sample lead data. The research confirms that Phase 6 established all necessary patterns (recipe content DTOs, TenantProvisioningService extension points, migration-based seeding) and Phase 7 is purely mechanical: create two migrations (one for Automobile, one for Education), extend `RecipeContentModel` to include sample leads, extend `TenantProvisioningService.SeedTenantDataAsync` to create leads during provisioning, and write integration tests.

The decisions from CONTEXT.md are highly detailed and prescriptive (15 locked decisions covering exact stage names, field definitions, field types, workflow rules, role names, sample lead distribution, and fixed GUIDs). No significant architectural decisions remain — implementation is straightforward.

**Primary recommendation:** Follow the exact specifications in CONTEXT.md (D-01 through D-14). The infrastructure is ready. Add a `SampleLeadDefinition` DTO to `RecipeContentModel`, implement lead creation in `SeedTenantDataAsync` after stages are seeded, and seed both recipes via a single migration (two `InsertData` calls with fixed GUIDs).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions (D-01 through D-14)

**Automobile Dealership Recipe (D-01 through D-05):**
- D-01: Stages: Inquiry [Entry] → Test Drive [Active] → Negotiation [Active] → F&I [Active] → Sold [ClosedWon] → Lost [ClosedLost]
- D-02: Custom fields (6): Vehicle Make [Dropdown: Toyota, Honda, Ford, Chevrolet, BMW, Mercedes, Other], Vehicle Model [Text], Vehicle Year [Number], Budget Range [Dropdown: Under 20K, 20-35K, 35-50K, 50-75K, 75K+], Has Trade-In [Boolean], Preferred Contact [Dropdown: Phone, Email, Text, WhatsApp]
- D-03: Workflow rules (2): "Notify on Negotiation" (StatusChange trigger, notify Sales Manager when stage=Negotiation), "Flag Stale Inquiry" (TimeElapsed trigger, flag leads idle in Inquiry 48h+ as at-risk)
- D-04: Roles (3): Sales Manager ("Oversees deals, approvals, and team performance"), Sales Executive ("Handles walk-ins, test drives, and closing deals"), BDC Agent ("Manages inbound inquiries and schedules appointments")
- D-05: Fixed GUID: `00000000-0000-0000-0000-000000000002`, IndustrySlug: `automobile`

**Educational Institution Recipe (D-06 through D-10):**
- D-06: Stages: Inquiry [Entry] → Application [Active] → Under Review [Active] → Interview [Active] → Enrolled [ClosedWon] → Declined [ClosedLost]
- D-07: Custom fields (6): Program of Interest [Dropdown: Engineering, Business, Arts, Science, Medicine, Law, Education, Other], Grade/Year Level [Dropdown: K-5, 6-8, 9-12, Undergraduate, Graduate], Previous School [Text], Guardian Name [Text], Guardian Phone [Text], Scholarship Needed [Boolean]
- D-08: Workflow rules (2): "Notify on Review" (StatusChange trigger, notify Admissions Officer when stage=Under Review), "Flag Stale Inquiry" (TimeElapsed trigger, flag leads idle in Inquiry 7d+ as at-risk)
- D-09: Roles (3): Admissions Director ("Oversees admissions process and team performance"), Admissions Officer ("Reviews applications and conducts interviews"), Academic Counselor ("Guides prospective students through program selection")
- D-10: Fixed GUID: `00000000-0000-0000-0000-000000000003`, IndustrySlug: `education`

**Sample Lead Data (D-11 through D-14):**
- D-11: 5 sample leads per recipe, spread across active stages (2 in Entry, 1 in subsequent Active stages, 0 in terminal stages)
- D-12: Realistic placeholder names, fictional but real-sounding. Use `@example.com` emails and `+1-555-0xxx` phone numbers. Diverse names.
- D-13: Custom field values populated with domain-appropriate data (e.g., Vehicle Make: Honda)
- D-14: Lead source for all sample leads: `WebForm`

**Seeding Strategy (D-15 through D-18):**
- D-15: Domain recipes seeded via EF Core migration (same InsertData pattern as Blank recipe). Fixed GUIDs ensure deterministic provisioning.
- D-16: Single migration `SeedDomainRecipes` inserts both Automobile and Education recipes in one migration.
- D-17: Add `SampleLeadDefinition` DTO to `RecipeContentModel` with new `SampleLeads` section (firstName, lastName, email, mobile, source, stageName, customFieldValues).
- D-18: `TenantProvisioningService.SeedTenantDataAsync` extended to process `SampleLeads` — creates Lead entities mapped to correct pipeline stages by name matching.

### Claude's Discretion

- Exact sample lead names, emails, phone numbers (following D-12 guidelines)
- Custom field values for each sample lead (following D-13 guidelines)
- ConditionJson and ActionJson structure for workflow rules (following existing WorkflowRule patterns)
- SampleLeadDefinition DTO design details (property types, nullability)
- Migration file naming and timestamp
- Test structure and assertions for recipe content verification
- Order of operations for seeding leads (after stages are created, so PipelineStageId can be resolved)

### Deferred Ideas (OUT OF SCOPE)

- Recipe selection during signup UI — Phase 8 scope (ONBD-01)
- Recipe preview endpoint — Phase 8 scope (ONBD-02)
- Admin API for recipe CRUD — Phase 8 scope (RADM-01, RADM-02, RADM-03)
- Recipe upgrade/migration for existing tenants — v1.2 (Out of Scope)
- Additional industry recipes beyond Automobile and Education — future milestone

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RCNT-01 | Automobile Dealership recipe includes road-to-the-sale pipeline stages, vehicle-specific custom fields, follow-up workflow rules, and sales team roles | Seeding via migration with fixed GUID; RecipeContentModel extended to include exact field/rule/role definitions per D-02, D-03, D-04; TenantProvisioningService applies all content to tenant DB |
| RCNT-02 | Educational Institution recipe includes admissions funnel stages, student-specific custom fields, notification workflow rules, and admissions team roles | Seeding via migration with fixed GUID; RecipeContentModel extended to include exact field/rule/role definitions per D-07, D-08, D-09; TenantProvisioningService applies all content to tenant DB |
| RCNT-03 | Each recipe includes sample lead data so the tenant sees a working pipeline immediately after provisioning | SampleLeadDefinition DTO added to RecipeContentModel; TenantProvisioningService.SeedTenantDataAsync creates leads after stages seeded; 5 leads per recipe spread per D-11 |

</phase_requirements>

## Standard Stack

### Core (No new dependencies)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| EF Core | 10.0.5 | ORM — migrations, JSONB storage | Phase 6 established; no new packages needed |
| Npgsql | 10.0.1 | PostgreSQL driver with JSONB support | Matches EF Core 10.0.5; no new packages needed |
| System.Text.Json | — | JSONB serialization in RecipeContentModel | Native to .NET; already used throughout codebase |

### Testing (Existing)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3 | Test framework | Existing; recipe content tests follow TenantProvisioningTests pattern |
| Testcontainers.PostgreSql | 4.3.0 | Real PostgreSQL in tests | Required for integration tests with real seeding |
| Moq | 4.20.72 | Mock framework | Optional; for mocking recipe lookups if needed |

**Installation:** No new packages required. Phase 7 uses only Phase 6 infrastructure.

**Version verification:** EF Core 10.0.5 (verified in CLAUDE.md and IronMonkey.Data.csproj). Npgsql 10.0.1 (verified matching).

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.Data/
├── RecipeContent/
│   ├── RecipeContentModel.cs      # EXTEND: Add SampleLeadDefinition class + SampleLeads property
│   └── [other sections unchanged]
├── Migrations/Central/
│   └── 20260326XXXXXX_SeedDomainRecipes.cs    # NEW: Insert Automobile + Education recipes
└── [other entities unchanged]

IronMonkey.ApiService/
├── Authentication/Services/
│   └── TenantProvisioningService.cs           # EXTEND: Add lead seeding logic in SeedTenantDataAsync

IronMonkey.Tests/Integration/
├── AutomobileRecipeProvisioningTests.cs       # NEW: Verify Automobile recipe content
├── EducationRecipeProvisioningTests.cs        # NEW: Verify Education recipe content
└── [existing tests unchanged]
```

### Pattern 1: Recipe Content DTO Extension (SampleLeadDefinition)

**What:** Add a new DTO class to `RecipeContentModel` to represent sample lead definitions in recipe JSON. The lead definition includes: firstName, lastName, email, mobile, source (as enum string: "WebForm"), stageName (matched to PipelineStageDefinition Name), and a dictionary of custom field values keyed by field name.

**When to use:** Whenever recipe templates need to include seed data beyond configuration (stages, fields, rules, roles).

**Example:**

```csharp
// Source: CONTEXT.md D-17
public sealed class RecipeContentModel
{
    public List<PipelineStageDefinition> PipelineStages { get; set; } = [];
    public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];
    public List<WorkflowRuleDefinition> WorkflowRules { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
    public List<SampleLeadDefinition> SampleLeads { get; set; } = [];  // NEW
}

public sealed class SampleLeadDefinition
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Source { get; set; } = "WebForm";  // LeadSource enum as string
    public string StageName { get; set; } = string.Empty;  // Matched to PipelineStageDefinition.Name
    public Dictionary<string, object?> CustomFieldValues { get; set; } = [];  // fieldName -> value mapping
}
```

### Pattern 2: Lead Seeding in TenantProvisioningService

**What:** Extend `SeedTenantDataAsync` to create Lead entities from the recipe's SampleLeads section after pipeline stages are created. Uses a name → stage ID mapping to resolve `StageName` to PipelineStageId.

**When to use:** When seed data depends on previously created entities (leads depend on stages, which depend on orders and field definitions).

**Example:**

```csharp
// Source: CONTEXT.md D-18, existing pattern from TenantProvisioningService
private async Task SeedTenantDataAsync(
    TenantDbContext db,
    Tenant tenant,
    SignupRequest signupRequest,
    Guid? recipeId,
    CancellationToken cancellationToken)
{
    // ... existing code: load recipe, seed stages, fields, rules ...

    // NEW: Seed sample leads (after stages are created so we can resolve stage names to IDs)
    var leads = content?.SampleLeads ?? [];
    if (leads.Count > 0)
    {
        // Build a mapping of stage name -> PipelineStageId
        var stageMap = await db.PipelineStages
            .AsNoTracking()
            .Where(s => s.TenantId == tenant.Id)
            .ToDictionaryAsync(s => s.Name, s => s.Id, cancellationToken);

        foreach (var leadDef in leads)
        {
            // Resolve stage name to ID; skip if stage not found
            if (!stageMap.TryGetValue(leadDef.StageName, out var stageId))
                continue;

            // Parse LeadSource enum
            var source = Enum.TryParse<LeadSource>(leadDef.Source, out var parsedSource)
                ? parsedSource
                : LeadSource.WebForm;

            // Create lead
            var lead = Lead.Create(tenant.Id, leadDef.FirstName, leadDef.LastName,
                leadDef.Mobile, leadDef.Email, source, stageId);

            // Populate custom field values if provided
            if (leadDef.CustomFieldValues?.Count > 0)
            {
                foreach (var kvp in leadDef.CustomFieldValues)
                {
                    lead.CustomFields.Set(kvp.Key, kvp.Value);
                }
            }

            db.Leads.Add(lead);
        }
    }

    // Single SaveChangesAsync — full atomicity (existing)
    await db.SaveChangesAsync(cancellationToken);
}
```

### Pattern 3: Fixed GUID Migration for Deterministic Recipes

**What:** Use `InsertData` in migrations with hardcoded GUIDs (Automobile: `00000000-0000-0000-0000-000000000002`, Education: `00000000-0000-0000-0000-000000000003`) to ensure recipes are seeded deterministically and can be referenced in provisioning code without database lookups.

**When to use:** Platform-level templates that multiple tenants will select from. Fixed GUIDs enable deterministic behavior.

**Example:**

```csharp
// Source: Phase 6 pattern (SeedBlankRecipe.cs), applied to domain recipes
// In 20260326XXXXXX_SeedDomainRecipes.cs:

protected override void Up(MigrationBuilder migrationBuilder)
{
    var automobileRecipeId = new Guid("00000000-0000-0000-0000-000000000002");
    var educationRecipeId = new Guid("00000000-0000-0000-0000-000000000003");
    var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Automobile recipe ContentJson with all 6 stages, 6 fields, 2 rules, 3 roles, 5 sample leads
    var automobileContentJson = "{ ... PascalCase JSON with exact content per D-02, D-03, D-04, D-11-14 ... }";

    // Education recipe ContentJson
    var educationContentJson = "{ ... PascalCase JSON with exact content per D-07, D-08, D-09, D-11-14 ... }";

    migrationBuilder.InsertData(
        table: "industry_recipes",
        columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
        values: new object[] { automobileRecipeId, "Automobile Dealership", "Complete sales pipeline for vehicle dealerships", "automobile", "icon-automobile", false, true, 1, automobileContentJson, now, now, false });

    migrationBuilder.InsertData(
        table: "industry_recipes",
        columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
        values: new object[] { educationRecipeId, "Educational Institution", "Complete admissions pipeline for schools and universities", "education", "icon-education", false, true, 1, educationContentJson, now, now, false });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DeleteData(table: "industry_recipes", keyColumn: "Id", keyValue: automobileRecipeId);
    migrationBuilder.DeleteData(table: "industry_recipes", keyColumn: "Id", keyValue: educationRecipeId);
}
```

### Anti-Patterns to Avoid

- **Don't create recipe roles dynamically during seeding:** Roles in the recipe definition are just role names/descriptions for documentation. Actual Role entities are created by a separate migration (Phase 6). The RoleDefinition in recipe content is informational only.
- **Don't modify existing Lead entity fields:** Lead.Create signature is locked — add custom field values via the `CustomFields` property after creation, don't extend the factory signature.
- **Don't skip stages in the migration content:** All 6 stages for each recipe must be in the seed JSON, in the correct order, with correct StageType values. Order property is critical for pipeline display.
- **Don't use null for custom field values:** Use empty Dictionary if no values provided. The pattern mirrors CustomFieldValues.Values which is a Dictionary<string, object?>.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Recipe JSON serialization | Hand-written JSON strings | System.Text.Json + RecipeContentModel DTO | Automatic, type-safe, maintainable; prevents typos in field/role names |
| Stage name → ID mapping during lead seeding | Manual loop with if-else checks | Dictionary<string, Guid> from db.PipelineStages | Safe, efficient, handles missing stages gracefully |
| Workflow rule conditions/actions | Custom expression parser | Plain JSON strings (ConditionJson, ActionJson) | Existing pattern in WorkflowRule entity; Phase 8 or later defines schema |
| Lead source conversion | String parsing error handling | Enum.TryParse with fallback to LeadSource.WebForm | Standard .NET pattern; handles invalid enum values from JSON |
| Custom field value typing | Generic object storage | Dictionary<string, object?> in CustomFieldValues | Matches existing CustomFieldValues implementation; flexible for any field type |

**Key insight:** The recipe infrastructure (Phase 6) is complete and well-designed. Phase 7 is purely content definition and data seeding — there are no architectural problems to solve. Avoid adding "smart" logic; stick to mechanical transformation from recipe JSON to tenant entities.

## Runtime State Inventory

**Trigger:** Phase 7 is NOT a rename/refactor/migration phase. This section is skipped.

However, for reference: after Phase 7 recipes are seeded, there will be:
- 2 new IndustryRecipe rows in CentralDbContext.IndustryRecipes (Automobile and Education) with fixed GUIDs
- Tenant.AppliedRecipeId column will track which recipe was used during provisioning (already wired in Phase 6)
- Sample leads will be created in tenant DBs when provisioning with either recipe

No existing data is renamed or migrated.

## Common Pitfalls

### Pitfall 1: Custom Field Value Keys Don't Match Field Names

**What goes wrong:** Sample leads include custom field values like `{ "VehicleMake": "Honda" }`, but the custom field definition is named `"Vehicle Make"` (with space). The key comparison fails because field names are exact strings.

**Why it happens:** When manually crafting sample lead data, it's easy to use camelCase or abbreviated names instead of matching the exact field names from the CustomFieldDefinitionDto list.

**How to avoid:** Before seeding sample leads, verify that every custom field value key exactly matches a CustomFieldDefinitionDto.FieldName in the same recipe. Whitespace matters.

**Warning signs:** Tenant sees sample leads with empty or missing custom field values after provisioning. The leads exist but appear incomplete.

### Pitfall 2: Lead Seeding Fails Silently When Stage Names Don't Match

**What goes wrong:** A sample lead references `StageName = "New Deal"`, but the recipe defines a stage named `"Negotiation"`. The stage lookup returns null, the lead is skipped, and the sample data is incomplete.

**Why it happens:** Manual typos in StageName values when crafting sample lead JSON. Or, the stage name list changes but sample leads aren't updated.

**How to avoid:** Test the stage name → ID mapping explicitly in tests. Use exact strings from PipelineStageDefinition.Name when creating sample leads. Add assertions in tests to verify the expected number of leads were created.

**Warning signs:** Recipe-provisioned tenant has fewer sample leads than expected. Logs show the lead seeding loop but fewer leads appear in DB.

### Pitfall 3: JSON Serialization Uses camelCase Instead of PascalCase

**What goes wrong:** When manually writing ContentJson strings for the migration, properties are serialized as `"pipelineStages"` instead of `"PipelineStages"`. JSON deserialization fails because System.Text.Json expects PascalCase by default.

**Why it happens:** Default convention confusion — camelCase is common in JavaScript but C# and System.Text.Json default to property name matching (PascalCase for C# properties).

**How to avoid:** Verify JSON keys match the C# property names exactly: `PipelineStages`, `CustomFields`, `WorkflowRules`, `Roles`, `SampleLeads` (all PascalCase). Test JSON round-trip deserialization in unit tests.

**Warning signs:** Recipe loading throws JsonException during deserialization. Check the JSON string in the migration for camelCase keys.

### Pitfall 4: Workflow Rule Conditions Reference Non-Existent Fields

**What goes wrong:** A workflow rule has `ConditionJson = "{ \"fieldId\": \"nonexistent-field\" }"`, but that field isn't defined in the recipe. The rule parses but fails at runtime when the field is missing.

**Why it happens:** Manual ConditionJson crafting without validation. Phase 8 will define the schema; Phase 7 just stores JSON strings.

**How to avoid:** Keep ConditionJson and ActionJson simple for Phase 7. For "Notify on Negotiation", use minimal JSON (e.g., `{ "stage": "Negotiation" }`) that doesn't reference fields. For "Flag Stale Inquiry", use `{ "stage": "Inquiry", "hours": 48 }` — time-elapsed rules don't need field references.

**Warning signs:** Workflow rules are created but don't fire. Conditions are parsed but never evaluated correctly because the JSON doesn't match expected schema. (This won't be obvious until Phase 8 implements rule evaluation.)

### Pitfall 5: Role Names in Recipe Don't Match Actual Roles Created

**What goes wrong:** The Automobile recipe defines roles: `Sales Manager`, `Sales Executive`, `BDC Agent`. But the migration that creates these Role entities (Phase 6) uses IDs 1, 201, 301, 302 for `SuperAdmin`, `Admin`, `Owner`, `TeleCaller`. The recipe roles are never created as actual Role entities.

**Why it happens:** Recipe RoleDefinition is informational only in Phase 7 — it's not meant to create Role entities. Actual roles are seeded via a separate migration. This can be confusing because the recipe appears to define roles but they don't actually exist in the database.

**How to avoid:** Clarify in code comments: "Recipe RoleDefinition is for documentation/display only. Actual Role entities are created by the Phase 6 migration and won't include domain-specific roles until Phase 8 admin API allows creation." For Phase 7, the domain-specific role names in the recipe are aspirational — they describe what roles should exist, but the tenant can only use the existing Admin role until Phase 8 implements custom role creation.

**Warning signs:** Sample leads can't be assigned to "Sales Manager" role because that role doesn't exist. (Phase 7 sample leads aren't assigned to roles anyway, so this won't surface immediately.)

### Pitfall 6: Sample Leads Span More or Fewer Stages Than Specified

**What goes wrong:** D-11 specifies "2 in Entry, 1 in subsequent Active stages" but the 5 sample leads are distributed as "5 in Inquiry, 0 in Test Drive, 0 in Negotiation, ...". The tenant sees an unbalanced pipeline that doesn't showcase all active stages.

**Why it happens:** Manual counting errors when crafting sample lead data. Or, unclear interpretation of "subsequent Active stages".

**How to avoid:** D-11 is precise: "2 leads in Entry stage, then 1 lead in each subsequent Active stage (not including terminal stages)." For Automobile: Inquiry(2), Test Drive(1), Negotiation(1), F&I(1), Sold(0), Lost(0). For Education: Inquiry(2), Application(1), Under Review(1), Interview(1), Enrolled(0), Declined(0). Verify counts in tests.

**Warning signs:** Tenant sees sample pipeline with only 1 or 2 leads instead of 5. Or leads are all in one stage, giving a false impression of a stuck pipeline.

## Code Examples

Verified patterns from Phase 6 and existing codebase:

### Example 1: Deserializing Recipe Content in TenantProvisioningService

```csharp
// Source: IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs, line 125-126
if (recipeId.HasValue)
{
    var recipe = await _centralDb.IndustryRecipes
        .AsNoTracking()
        .SingleOrDefaultAsync(r => r.Id == recipeId.Value, cancellationToken);

    if (recipe == null)
        throw new InvalidOperationException($"Recipe {recipeId} not found.");

    content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson)
        ?? new RecipeContentModel();
}
```

### Example 2: Creating Pipeline Stages from Recipe Definition

```csharp
// Source: IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs, line 130-137
var stages = content?.PipelineStages ?? [];
foreach (var stageDef in stages)
{
    var stageType = Enum.TryParse<StageType>(stageDef.StageType, out var parsed)
        ? parsed
        : StageType.Active;
    var stage = PipelineStage.Create(tenant.Id, stageDef.Name, stageDef.Order, stageType);
    db.PipelineStages.Add(stage);
}
```

### Example 3: Seeding Blank Recipe via Migration (PascalCase JSON)

```csharp
// Source: IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.cs, line 13-20
var blankRecipeId = new Guid("00000000-0000-0000-0000-000000000001");
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var contentJson = "{\"PipelineStages\":[{\"Name\":\"New\",\"Order\":0,\"StageType\":\"Entry\"}],\"CustomFields\":[],\"WorkflowRules\":[],\"Roles\":[]}";

migrationBuilder.InsertData(
    table: "industry_recipes",
    columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
    values: new object[] { blankRecipeId, "Blank/Custom", "Start with a clean workspace. One default pipeline stage included.", "blank", "icon-blank", true, true, 1, contentJson, now, now, false });
```

### Example 4: Lead Creation with Custom Field Values

```csharp
// Source: IronMonkey.Data/Entities/Lead.cs, line 41-44 + CustomFieldValues pattern
var lead = Lead.Create(tenantId, "John", "Doe", "+1-555-0100", "john@example.com", LeadSource.WebForm, stageId);
lead.CustomFields.Set("Vehicle Make", "Honda");
lead.CustomFields.Set("Vehicle Model", "Civic");
lead.CustomFields.Set("Vehicle Year", 2023);
db.Leads.Add(lead);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual tenant provisioning with hardcoded defaults | Recipe-based provisioning with template content | Phase 6 (2026-03-26) | Tenants can now select domain-specific configurations during signup; seed data makes workspace immediately useful |
| Recipes as ad-hoc configurations | Recipes as immutable JSONB snapshots with versioning | Phase 6 (2026-03-26) | Templates can be evolved over time; version tracking enables future upgrade tooling |
| Sample data created manually per tenant | Sample data seeded from recipe template | Phase 7 (2026-03-26) | Non-empty workspace out of the box; reduces onboarding friction |

**Deprecated/outdated:** None for Phase 7. This is new functionality.

## Open Questions

1. **Workflow rule schema for ConditionJson and ActionJson (Phase 8 scope)**
   - What we know: Workflow rules have ConditionJson and ActionJson properties; Phase 7 documents the structure via examples in the recipe definitions
   - What's unclear: Exact schema for condition/action payloads; Phase 8 will define this when implementing rule evaluation
   - Recommendation: For Phase 7, keep ConditionJson/ActionJson minimal and self-documenting (e.g., `{ "stage": "Negotiation" }` for "Notify on Negotiation" rule). Don't over-architect; Phase 8 can refactor as needed.

2. **Custom Role assignment for sample leads (Phase 8+ scope)**
   - What we know: Sample leads are created with basic fields (name, email, phone, custom field values) but are not assigned to domain-specific roles
   - What's unclear: Whether Phase 8 will extend sample leads to assign them to roles, or if role assignment is tenant-driven
   - Recommendation: For Phase 7, leave sample leads without role assignments. The leads exist and show the pipeline structure; role assignment is a later feature.

3. **Field name exact matching for custom field values (Phase 7 implementation detail)**
   - What we know: Custom field values are stored as Dictionary<string, object?> keyed by field name
   - What's unclear: Should the key be the exact field name from CustomFieldDefinitionDto.FieldName, or a normalized version?
   - Recommendation: Use exact field names (with spaces if defined that way). This matches the CustomFieldValues.Set(fieldId, value) pattern used elsewhere in the codebase.

## Environment Availability

**Step 2.6: SKIPPED** — Phase 7 is a code/config-only change. No external dependencies beyond the existing PostgreSQL and .NET runtime (already verified in Phase 6). No new tools, services, runtimes, or databases required.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | None (implicit discovery via *.cs test files) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Recipe" -x` |
| Full suite command | `dotnet test IronMonkey.Tests -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RCNT-01 | Automobile recipe seeds 6 pipeline stages with correct names and types | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests.SeedsAllStages" -x` | ❌ Wave 0 |
| RCNT-01 | Automobile recipe seeds 6 custom fields with correct types and options | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests.SeedsAllCustomFields" -x` | ❌ Wave 0 |
| RCNT-01 | Automobile recipe seeds 2 workflow rules with correct triggers | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests.SeedsWorkflowRules" -x` | ❌ Wave 0 |
| RCNT-01 | Automobile recipe seeds 3 roles with correct names | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests.SeedsRoles" -x` | ❌ Wave 0 |
| RCNT-02 | Educational Institution recipe seeds 6 pipeline stages with correct names and types | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests.SeedsAllStages" -x` | ❌ Wave 0 |
| RCNT-02 | Educational Institution recipe seeds 6 custom fields with correct types and options | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests.SeedsAllCustomFields" -x` | ❌ Wave 0 |
| RCNT-02 | Educational Institution recipe seeds 2 workflow rules with correct triggers | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests.SeedsWorkflowRules" -x` | ❌ Wave 0 |
| RCNT-02 | Educational Institution recipe seeds 3 roles with correct names | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests.SeedsRoles" -x` | ❌ Wave 0 |
| RCNT-03 | Automobile recipe seeds 5 sample leads distributed across active stages (2 Entry, 1 each Active) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests.SeedsSampleLeads" -x` | ❌ Wave 0 |
| RCNT-03 | Educational Institution recipe seeds 5 sample leads distributed across active stages (2 Entry, 1 each Active) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests.SeedsSampleLeads" -x` | ❌ Wave 0 |
| RCNT-03 | Sample leads have custom field values populated | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~.*RecipeProvisioningTests.SampleLeadCustomFields" -x` | ❌ Wave 0 |
| ONBD-04 | All recipe-seeded configuration is fully modifiable by the tenant after provisioning | Manual-only | Tenant can edit stages, fields, rules, roles via API (tested in Phase 8+) | ✅ Deferred |

### Sampling Rate

- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Recipe" -x` (run recipe tests)
- **Per wave merge:** `dotnet test IronMonkey.Tests -x` (full suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `IronMonkey.Tests/Integration/AutomobileRecipeProvisioningTests.cs` — verifies RCNT-01 (stages, fields, rules, roles, sample leads)
- [ ] `IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs` — verifies RCNT-02 (stages, fields, rules, roles, sample leads)
- [ ] `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` — extend with SampleLeadDefinition class and SampleLeads property
- [ ] `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` — extend SeedTenantDataAsync to create leads from recipe
- [ ] `IronMonkey.Data/Migrations/Central/20260326XXXXXX_SeedDomainRecipes.cs` — insert Automobile and Education recipes

## Sources

### Primary (HIGH confidence)

- **Phase 6 Research & Verification** (2026-03-26) — Recipe infrastructure model confirmed; RecipeContentModel structure, TenantProvisioningService patterns, migration strategy verified
- **CONTEXT.md Phase 7** (2026-03-26) — All 18 design decisions (D-01 through D-18) locked; exact stage names, field definitions, workflow rules, sample lead distribution specified
- **IronMonkey.Data/Entities/Lead.cs** — Lead.Create factory and CustomFieldValues storage pattern confirmed
- **IronMonkey.Data/Entities/PipelineStage.cs** — StageType enum (Entry, Active, ClosedWon, ClosedLost) confirmed
- **IronMonkey.Data/Entities/CustomFieldDefinition.cs** — CustomFieldType enum and Create factory confirmed
- **IronMonkey.Data/Entities/WorkflowRule.cs** — WorkflowTrigger enum (FieldChange, StatusChange, TimeElapsed) confirmed
- **IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs** — SeedTenantDataAsync pattern and stage/field/rule seeding logic confirmed
- **IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.cs** — Migration pattern for recipe seeding (InsertData, PascalCase JSON, fixed GUID) confirmed

### Secondary (MEDIUM confidence)

- None — all critical information sourced from primary (locked decisions + verified code).

### Tertiary (LOW confidence)

- None — no speculative research needed. Phase 6 infrastructure is complete and tested.

## Metadata

**Confidence breakdown:**
- Standard Stack: HIGH — No new dependencies; Phase 6 established all patterns
- Architecture: HIGH — Phase 6 verified recipe infrastructure; Phase 7 is mechanical seeding
- Pitfalls: HIGH — Identified from codebase patterns and decision specificity
- Test Framework: HIGH — Existing xUnit setup; recipe tests follow TenantProvisioningTests pattern
- Runtime State: HIGH — Not a rename/refactor; no existing data affected

**Research date:** 2026-03-26
**Valid until:** 2026-04-02 (stable domain; locked decisions reduce risk of outdated research)

**Next Step:** Planning phase ready. All decisions locked; no architectural research gaps remain.
