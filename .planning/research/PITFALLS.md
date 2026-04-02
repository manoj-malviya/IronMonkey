# Domain Pitfalls: Industry Recipe-Based Tenant Onboarding

**Domain:** Multi-tenant CRM with recipe/template-based provisioning for tenant onboarding
**Researched:** 2026-03-24
**Context:** IronMonkey v1.1 — Adding industry recipes (Automobile, Education) and blank/custom option to tenant provisioning flow
**Confidence:** HIGH (validated against EF Core seeding patterns, SaaS provisioning best practices, Azure/AWS documentation)

---

## Critical Pitfalls

Mistakes that cause data corruption, provisioning rewrites, or tenant onboarding failures. These must be prevented in v1.1 foundation.

---

### Pitfall 1: Non-Idempotent Recipe Application — Duplicate Records or Partial Seeding

**What goes wrong:**

Recipe application (seeding pipeline stages, custom fields, workflow rules, roles) fails halfway through. Tenant retries provisioning. Result:
- Duplicate records created (stages seeded twice, causing foreign key violations)
- Partial application (stages exist, roles don't; tenant can't assign users)
- Orphaned records (field created, rule that references it fails; field stays, rule doesn't)
- Tenant database left in inconsistent state

Example scenario:
1. Recipe seeding: Create stages ✓, Create fields ✓, Create rules ✓, Create roles ✓, Assign permissions ✗ (connection timeout)
2. Tenant DB now has incomplete configuration
3. Tenant logs in, sees roles without permissions. Support required.

**Why it happens:**

Recipe seeding typically done via:
- EF Core `HasData` in migrations (hard-coded primary keys, requires migration for each data change)
- Imperative seeding in provisioning service without checking if records already exist
- Multiple sequential INSERT statements without atomic transaction wrapping
- No idempotency token or application state tracking

If one step fails, previous steps already committed to DB. No rollback. No way to safely retry.

**Consequences:**

- Tenant database stuck in inconsistent state (partially seeded recipe)
- Retry fails due to duplicate key violations (second attempt tries to insert same stage again)
- Manual intervention required: support engineer has to clean up and reseed
- Tenant onboarding blocked until data manually repaired
- Perceived system unreliability (tenant loses trust)

**Prevention:**

1. **Wrap entire recipe application in single database transaction**
   ```csharp
   using (var transaction = context.Database.BeginTransaction())
   {
     try
     {
       SeedPipelineStages(recipe);     // All or none
       SeedCustomFields(recipe);
       SeedWorkflowRules(recipe);
       SeedRoles(recipe);
       SeedPermissions(recipe);
       context.SaveChanges();
       transaction.Commit();
     }
     catch
     {
       transaction.Rollback();
       throw;  // Provisioning fails, tenant DB left untouched
     }
   }
   ```

2. **Use existence checks before each insert**
   ```csharp
   // Safe to retry: if stage already exists, skip it
   if (!context.PipelineStages.Any(s => s.Code == "QUALIFIED" && s.TenantId == tenantId))
   {
     context.PipelineStages.Add(new PipelineStage { ... });
   }
   ```

3. **Implement idempotency token or recipe version tracking**
   - Store `RecipeApplicationId` (GUID) on tenant record
   - If provisioning retried with same recipe, check: `tenant.LastRecipeApplicationId == recipeApplicationId`
   - If match, skip re-seeding (already applied)

4. **Order seeding by dependencies**
   - Stages before fields that reference stages
   - Fields before rules that reference fields
   - Roles before permissions
   - Validate prerequisites exist before seeding dependents

5. **Test idempotency explicitly**
   ```
   Test: Apply recipe → verify state X
   Test: Apply recipe again with same tenant → verify state still X (no duplicates)
   Test: Apply recipe, fail at step 4, retry → verify entire recipe rolls back, state unchanged
   ```

**Detection:**

- Monitoring: Count pipeline stages per tenant — should match recipe definition exactly
- Logs: Record recipe application start/end with transaction state
- Alerts: Provisioning transaction rolled back; recipe application failed
- Validation: Query seeded records post-provisioning, verify no duplicates exist

**Phase responsibility:** **v1.1 Recipe Foundation — CRITICAL** — Implement transactional seeding before any recipe is released.

---

### Pitfall 2: Recipe Data Model Confusion — Templates vs Instances, Central vs Tenant DB

**What goes wrong:**

Ambiguity in recipe data model design leads to implementation conflicts:

**Scenario A (Template in Central DB, Mutable):**
```
RecipeTemplate { Id, Name: "Automobile", Version: "1.0", Definition: [...stages, fields, rules...] }
```
You later update the template for all tenants. But Tenant A provisioned 3 weeks ago with old version. Now:
- New Tenant D gets different recipe than Tenant A
- Tenant A and D have same "Automobile" recipe but different stages
- Reports mixing data from both versions break
- You can't roll back Tenant A's recipe without breaking Tenant D's

**Scenario B (Instance in Tenant DB, Copied at Provisioning):**
```
Tenant A: PipelineStages { [Lead, Qualified, Proposal, Won] }
Tenant B: PipelineStages { [Lead, Qualified, Proposal, Won] }
```
Both are copies. Later you realize Proposal stage should be split into "Proposal" and "Negotiation". But:
- Updating the central recipe template doesn't affect existing tenants
- No path to upgrade Tenant A without breaking customizations
- Two tenants with same industry have diverged recipes (configuration drift)

**Why it happens:**

Recipe design not clarified upfront. It's unclear whether recipe is:
- A shared, immutable template (changes affect all tenants using it)
- A per-tenant configuration (can be modified independently)

Code written without clear ownership:
- Recipe updates logic assumes template is mutable
- Customization logic assumes recipe is in tenant DB
- Queries check central DB, but data actually in tenant DB

**Consequences:**

- Code confusion: "Update recipe" — update in central or tenant DB?
- Data structure unclear: Seeding logic copies template to tenant, but also references central template
- Customization broken: Tenant modifies recipe; either affects other tenants or doesn't persist
- Reporting broken: Recipe data queried from wrong database
- Recipe versioning impossible: no way to track which version tenant is using

**Prevention:**

1. **Document recipe ownership model clearly and upfront**
   ```
   CENTRAL DB: RecipeTemplate (immutable)
   - Stores reusable recipe definitions
   - Used at provisioning time only
   - Never updated after initial creation
   - Versioning: Recipe "Automobile v1.0", "Automobile v1.1"

   TENANT DB: PipelineStages, CustomFields, WorkflowRules, Roles, Permissions (mutable)
   - Tenant-specific instances created during provisioning
   - Tenant can freely customize after provisioning
   - No foreign keys back to central recipe
   - Tenant DB is self-contained
   ```

2. **Design schema to enforce ownership**
   ```csharp
   // Central DB
   public class RecipeTemplate
   {
     public Guid Id { get; set; }
     public string Name { get; set; }  // "Automobile"
     public string Version { get; set; } // "1.0"
     public RecipeDefinition Definition { get; set; } // JSON with stages, fields, rules
   }

   // Tenant DB (NOT a foreign key reference to RecipeTemplate)
   public class PipelineStage
   {
     public Guid Id { get; set; }
     public string Code { get; set; }
     public string Name { get; set; }
     // NO: public Guid RecipeTemplateId { get; set; }  ← DON'T DO THIS
     // Template is for reference only; actual data is here
   }
   ```

3. **At provisioning, copy recipe to tenant DB, don't reference**
   ```csharp
   // Load recipe template from central DB
   var template = centralDb.RecipeTemplates.Find(recipeId);

   // Copy to tenant DB (transform template → actual entities)
   foreach (var stageInTemplate in template.Definition.Stages)
   {
     tenantDb.PipelineStages.Add(new PipelineStage
     {
       Code = stageInTemplate.Code,
       Name = stageInTemplate.Name,
       // ... other properties
     });
   }
   tenantDb.SaveChanges();
   // Done. Tenant DB is self-contained; no reference back to template.
   ```

4. **Document modification boundaries** in UI and API docs:
   ```
   "After provisioning, your tenant workspace is fully customizable.
    Pipeline stages, fields, rules, roles can be modified, added, deleted.
    Recipe is for initial setup only; modifications are permanent for your tenant."
   ```

5. **Plan for recipe versioning upfront** (even if not implemented in v1.1)
   ```
   Design allows: TenantRecipeVersion { TenantId, TemplateId, TemplateVersion }
   Enables future: Recipe upgrades, migration tooling in v1.2+
   ```

**Detection:**

- Code review: Is recipe queried from central or tenant DB?
- Schema review: Tenant entities have foreign keys back to central recipe? (Red flag)
- Test: Provisioning applies recipe to tenant; central recipe unchanged; tenant can modify stage name; check central DB unchanged
- Compare: Central RecipeTemplate vs Tenant PipelineStages — are they linked or independent?

**Phase responsibility:** **v1.1 Recipe Foundation — CRITICAL** — Clarify and enforce template vs instance before writing any seeding code.

---

### Pitfall 3: Industry-Specific Field/Stage Type and Data Accuracy Mismatches

**What goes wrong:**

Automobile recipe specifies:
- "Vehicle Type" — Expected: enum field with values ["SUV", "Sedan", "Truck", "Van"]
- "VIN" — Expected: unique, indexed string (vehicle identifier)
- "Service History" — Expected: multi-select, linked to Service records

But seeded incorrectly:
- "Vehicle Type" seeded as plain text field (not enum) → Sales rep enters "truck" (lowercase), doesn't match "Truck" in enum
- "VIN" seeded as text without unique constraint → Duplicate VINs allowed, deduplication breaks
- "Service History" seeded as text field (not a relation) → Can't query service history, reports fail

Or Education recipe specifies:
- "Degree Type" — Expected: enum ["Bachelor", "Master", "PhD"]
- "GPA" — Expected: decimal(3,2), required for Accepted stage
- "Enrollment Date" — Expected: date, used for cohort grouping

But seeded as:
- "Degree Type" as text field; enum values not enforced
- "GPA" as text field (not numeric); arithmetic comparisons in workflows fail
- "Enrollment Date" as string; date comparisons fail

**Why it happens:**

Recipe data (stages, fields, field types, constraints) is complex and domain-specific. Easy to:
- Confuse field type in seed data (intending enum, actually text)
- Forget constraints (unique, indexed, required by stage)
- Misspell enum values ("Suv" vs "SUV")
- Not understand industry conventions (automotive: VIN as unique identifier; education: GPA range 0-4.0)
- Recipe spec and actual EF Core field definitions diverge

**Consequences:**

- Bad data quality from day one (tenant starts with incorrect field types)
- Reports broken (grouping on text field that should be enum; "truck" vs "Truck" causes grouping failure)
- Workflow rules fail (rule tries to match enum value; field is text, comparison fails)
- Integrations fail (external system expects enum values; system sends freeform text)
- Tenant loses time fixing data or re-entering leads
- Trust in system drops immediately ("Why can't I group by vehicle type?")

**Prevention:**

1. **Define recipe spec clearly before implementation** — For each field in each recipe:
   ```
   Field: "Vehicle Type" (Automobile recipe)
   - Data Type: Enum
   - Enum Values: ["SUV", "Sedan", "Truck", "Van", "Luxury SUV"]
   - Required by Stage: "Proposal" onwards
   - Constraints: None
   - Description: "Classification of vehicle type"

   Field: "VIN" (Automobile recipe)
   - Data Type: String
   - Length: 17 (VIN is always 17 chars)
   - Required by Stage: "Lead" (always required)
   - Constraints: Unique, Indexed
   - Description: "Vehicle Identification Number — unique identifier for each vehicle"

   Field: "GPA" (Education recipe)
   - Data Type: Decimal(3,2)  ← 0-4.0 range
   - Min: 0.0, Max: 4.0
   - Required by Stage: "Accepted" onwards
   - Constraints: None
   - Description: "Grade Point Average"
   ```

2. **Validate recipe spec against EF Core model** — Before seeding:
   ```csharp
   var recipeSpec = RecipeDefinition.Load("automobile-v1.0.json");

   foreach (var fieldSpec in recipeSpec.Fields)
   {
     // Validate: field type in spec matches EF Core model
     // e.g., spec says "Enum", EF model has enum? Yes/No
     // spec says "Unique", DB has unique constraint? Yes/No
     ValidateFieldSpec(fieldSpec, tenantDbContext);
   }
   ```

3. **Industry review before release** — Before releasing Automobile or Education recipe:
   - Get feedback from 2-3 actual practitioners (dealership manager, admissions officer)
   - Validate: stages match their workflow, fields are correct types, enum values are realistic
   - Test: Can they use the recipe out of the box? (Without customization)

4. **Test recipe seeding produces correct schema**
   ```csharp
   [Fact]
   public void AutomobileRecipe_VIN_Field_IsUnique_And_Indexed()
   {
     var tenantDb = SetupTestTenantDb();
     ApplyRecipe("automobile-v1.0", tenantDb);

     // Query DB schema, validate VIN field
     var schema = tenantDb.Database.GetDbConnection() ... get table schema
     var vinColumn = schema.Columns.FirstOrDefault(c => c.Name == "VIN");
     Assert.NotNull(vinColumn);
     Assert.True(vinColumn.IsUnique);
     Assert.True(schema.Indexes.Any(i => i.Columns.Contains("VIN")));
   }
   ```

5. **Document recipe field mappings in tenant UI** — Show tenant:
   ```
   "Your Automobile workspace includes:
    - Vehicle Type (dropdown) — required
    - VIN (text) — unique identifier
    - Service History (linked records) — past service visits
    - Last Service Date (date) — when vehicle last serviced
   "
   ```

**Detection:**

- Test: Apply recipe, query tenant DB, validate field types match spec
- Monitoring: Dashboard grouping query fails; check field type in schema vs query expectation
- Alerts: Workflow rule references field with unexpected type; log warning
- Analytics: Are fields with wrong types used? (e.g., grouping by VIN field)

**Phase responsibility:** **v1.1 Automobile + Education Recipes — HIGH** — Get field definitions right before seeding. Include domain review in definition phase.

---

### Pitfall 4: Partial Provisioning Failure — Recipe Partially Applied, Tenant Stuck

**What goes wrong:**

Recipe application sequence (with transactional wrapper):
1. Create pipeline stages ✓
2. Create custom fields ✓
3. Create workflow rules ✓
4. Create roles ✓
5. Assign role permissions ✗ FAILS (PermissionCode enum value missing, or FK violation)

Transaction rolls back. But problem: user sees "Provisioning failed" message, but doesn't know which step failed or why. Retries provisioning. Same failure. Tenant now stuck — can't use system. Support required.

Different failure scenario:
- Database connection timeout midway through
- Concurrency issue (another tenant's migration locked the table)
- Validation error (recipe data invalid; constraint violation)

**Why it happens:**

Recipe seeding is multiple steps. Even with transaction wrapping, if error occurs, user doesn't know:
- Which step failed?
- Is it safe to retry?
- What's the current state?

Error messaging unclear: "Recipe provisioning failed" (not helpful).

**Consequences:**

- Tenant onboarding incomplete; system unusable
- Tenant receives cryptic error message
- Retry may fail with same or different error
- Support required to investigate or manually fix
- Trust in system damaged

**Prevention:**

1. **Wrap entire recipe in single transaction** (already mentioned in Pitfall 1, but critical here too)

2. **Provide detailed error messages**
   ```csharp
   catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
   {
     if (pgEx.SqlState == "23505")  // Unique violation
       throw new RecipeProvisioningException(
         "Recipe seeding failed: Duplicate stage found. " +
         $"Stage '{pgEx.ConstraintName}' already exists in your workspace.",
         RecipeProvisioningStep.PipelineStages, ex);
     // ... other constraint codes
   }
   catch (Exception ex)
   {
     throw new RecipeProvisioningException(
       $"Recipe seeding failed at step: {currentStep}. " +
       $"Error: {ex.Message}. Entire provisioning rolled back.",
       currentStep, ex);
   }
   ```

3. **Return structured error response to caller**
   ```csharp
   [HttpPost("tenants/{tenantId}/provision-recipe")]
   public async Task<IResult> ProvisionRecipe(Guid tenantId, string recipeId)
   {
     try
     {
       await provisioner.ApplyRecipe(tenantId, recipeId);
       return Results.Ok(new { message = "Recipe applied successfully" });
     }
     catch (RecipeProvisioningException ex)
     {
       return Results.BadRequest(new
       {
         error = "RecipeProvisioningFailed",
         message = ex.Message,
         failedStep = ex.Step,  // "PipelineStages", "Roles", etc.
         retryable = true
       });
     }
   }
   ```

4. **Log provisioning steps with state tracking**
   ```csharp
   logger.Information("Recipe provisioning started: tenant={TenantId}, recipe={RecipeId}",
     tenantId, recipeId);
   logger.Information("Recipe provisioning step: stages (1/5)", tenantId);
   // ... steps 2-5

   logger.Error("Recipe provisioning failed at step 5 (permissions): {@Exception}",
     ex, tenantId);
   ```

5. **Test failure scenarios explicitly**
   ```
   Test: Provision recipe; inject failure at step 1 → provisioning fails, rollback succeeds
   Test: Provision recipe; inject failure at step 3 → provisioning fails, rollback succeeds
   Test: Provision recipe; inject failure at step 5 → provisioning fails, rollback succeeds
   Test: Retry after failure → succeeds without duplicates
   ```

**Detection:**

- Monitoring: Recipe provisioning endpoint returns 500; log shows which step failed
- Alerts: Provisioning transaction rolled back; error details logged
- Support: Tenant receives actionable error message, can retry or contact support

**Phase responsibility:** **v1.1 Recipe Foundation — HIGH** — Implement detailed error handling and logging.

---

### Pitfall 5: Blank/Custom Template Unusable Out of the Box

**What goes wrong:**

Tenant chooses "Blank/Custom" at signup (no industry recipe). Provisioning creates empty tenant DB with:
- No pipeline stages
- No custom fields
- No workflow rules
- No roles

Tenant logs in and sees empty canvas. Tries to create a lead:
- → Fails (no stages to move lead through; can't save lead without stage)

Tries to assign user:
- → Fails (no roles defined; can't assign users without roles)

Tries to set up workflow:
- → Fails (no fields defined to trigger on)

Support ticket: "System is broken, can't do anything."

**Why it happens:**

Blank template treated as true blank slate: "Let tenant build everything from scratch." But system requires minimum configuration to function:
- At least one stage (leads must have state)
- At least one role (users must have role)
- Some way to define fields

**Consequences:**

- Poor first impression (system appears broken, unusable)
- High support overhead (every blank template user needs onboarding help)
- Tenant likely to give up or churn
- Trust damaged

**Prevention:**

1. **Define minimum viable blank template** — Even "blank" should bootstrap:
   ```
   Stages: "New" (required minimum)
   Roles: "Admin" (required minimum)
   Fields: None (fully customizable)
   Workflow Rules: None (optional)

   Guidance: "Your workspace is blank. Here's the minimum you need:
             1. Add at least one stage (e.g., 'Contacted', 'Proposal', 'Won')
             2. Define which fields you want to track
             3. Add team members and assign roles
             Start here: [Setup Wizard button]"
   ```

2. **Provide interactive setup wizard for blank template**
   ```
   After signup with blank template:
   Step 1: "Add your first pipeline stage" (user enters name)
   Step 2: "What fields do you want to track?" (checkboxes: Email, Phone, Notes, etc.)
   Step 3: "Invite team members" (add users)
   Step 4: "You're ready to go!"
   ```

3. **Or: Offer "Starter" template in addition to blank**
   ```
   Recipe options:
   - "Automobile" (pre-configured)
   - "Education" (pre-configured)
   - "Starter" (minimal config: 3 stages, 5 fields, 2 roles)
   - "Blank" (advanced users only)
   ```

4. **Validate minimum requirements before allowing tenant to proceed**
   ```csharp
   [HttpPost("tenants/{tenantId}/provision")]
   public async Task<IResult> ProvisionTenant(Guid tenantId, ProvisionRequest request)
   {
     if (request.RecipeId == "blank")
     {
       // Validate: blank template will have usable configuration
       var willBeUsable = ValidateBlankTemplateUsability(tenantId);
       if (!willBeUsable)
         return Results.BadRequest(new
         {
           error = "BlankTemplateUnusable",
           message = "Blank workspace requires at least one stage. " +
                    "Add a stage or choose an industry recipe.",
           suggestedRecipes = ["Starter", "Automobile"]
         });
     }
     // ... proceed with provisioning
   }
   ```

**Detection:**

- Test: Provision with blank template; try to create lead → should work (or show clear error with guidance)
- Monitoring: Blank template tenants → average first-action latency (should be < 5 min)
- Support tickets: "Can't do anything in blank template" (if common, design failed)

**Phase responsibility:** **v1.1 Recipe Selection — HIGH** — Blank template validation critical at signup.

---

### Pitfall 6: Recipe Updates After Tenants Already Provisioned — Drift and Upgradability

**What goes wrong:**

Recipe 1.0 (Automobile): Stages = ["Lead", "Qualified", "Proposal", "Won"]. Tenants A, B, C provisioned with this recipe.

Later, you release Recipe 1.1 (Automobile): Add "Negotiation" stage (between Proposal and Won). But:
- Tenant A, B, C still have 4 stages (old recipe)
- New Tenant D gets 5 stages (new recipe)
- Different "Automobile" tenants have different recipes → data inconsistency
- Reports that aggregate across all "Automobile" tenants break (mismatched stages)
- You can't apply new stages to A, B, C without breaking their customizations

Tenant A customized "Proposal" stage (renamed to "Price Negotiation"). You release Negotiation stage. Now:
- Should A migrate? But they already customized Proposal.
- If you force migration, A's customization is lost.
- If you don't migrate, A is stuck with old recipe.

**Why it happens:**

Recipe treated as immutable once applied. No versioning. No upgrade path for existing tenants. Recipe changes only apply to new tenants.

Design assumption: Recipe is static baseline, tenants customize after provisioning. But reality:
- Recipes improve (new best practices discovered)
- Recipes need to be consistent across same industry
- Tenants expect new recipe features to be available

**Consequences:**

- Tenant drift (recipes diverge over time; same industry has different data models)
- Data consistency issues in reporting (aggregate reports across "Automobile" tenants broken)
- Can't rollout improvements to all tenants
- Workflow rules become stale (reference stages that don't exist in newer recipe)
- Tenant confusion ("Why does the tutorial show a 'Negotiation' stage but I don't have one?")

**Prevention:**

1. **Design recipes with stable version numbers from the start**
   ```
   RecipeTemplate {
     Id: guid,
     Name: "Automobile",
     Version: "1.0",  // ← Track version
     ReleaseDate: 2026-03-24,
     Definition: { ... stages, fields, rules ... }
   }
   ```

2. **Track which recipe version each tenant used**
   ```csharp
   public class Tenant
   {
     public Guid Id { get; set; }
     public string Name { get; set; }
     public Guid RecipeTemplateId { get; set; }  // Which recipe?
     public string RecipeVersion { get; set; }   // Which version? "1.0"
     public DateTimeOffset RecipeAppliedAt { get; set; }
   }
   ```

3. **Provide recipe upgrade path**
   ```
   Tenant dashboard: "New Automobile Recipe v1.1 available"
   - [Review changes] button → shows diff (new stages, fields, rules)
   - [Upgrade] button → applies new recipe changes (non-breaking only)
   - [Not now] → skip upgrade, stay on v1.0

   For breaking changes:
   - [Schedule upgrade] → admin reviews changes, schedules at maintenance window
   - [Contact support] → guidance on migrating customizations
   ```

4. **Make recipe updates additive when possible**
   - Add new stages, don't delete old ones
   - Add new fields, don't delete old ones
   - Deprecate rather than remove
   - Example: "Negotiation" stage added between "Proposal" and "Won" (non-breaking)
   - Counter-example: Delete "Proposal" stage (breaking; affects all existing leads in Proposal)

5. **Document recipe version changelog**
   ```
   Automobile Recipe Changelog:
   - v1.0 (2026-03-24): Initial release
   - v1.1 (2026-06-24): Added "Negotiation" stage (non-breaking)
   - v1.2 (2026-09-24): Added "Extended Warranty" field (additive)
   ```

6. **Consider recipe migration tooling for v1.2+**
   ```
   Tool: Migrate tenant from v1.0 → v1.1
   - Analyze tenant's customizations
   - Map old stages to new stages (e.g., tenant's "Negotiation_old" → new "Negotiation")
   - Preserve workflow rules (update rule references to match new stages)
   - Validate: no orphaned rules or fields
   - Dry-run before applying
   ```

**Detection:**

- Query: `SELECT tenant_id, recipe_version FROM tenants GROUP BY recipe_version` — any mismatches?
- Logs: Workflow rule references stage that doesn't exist in tenant's recipe version
- Alerts: New tenant gets Recipe v1.1; month-old tenant still on v1.0 (drift detected)

**Phase responsibility:** **v1.1 Recipe Foundation — Design for versioning (don't implement migration tooling)** — v1.2+ Recipe Upgrades may need migration tooling.

---

## Moderate Pitfalls

Mistakes that cause data quality issues, poor UX, or operational overhead. Impact is significant but recoverable.

---

### Pitfall 7: Recipe-Specific Field Metadata Not Captured or Enforced

**What goes wrong:**

Automobile recipe includes "Vehicle VIN" — unique identifier for each vehicle. Seeded as:
```csharp
new CustomField {
  Name = "VIN",
  Type = "Text",
  // Missing: IsUnique = true, IsIndexed = true
}
```

Tenant tries to:
- Use VIN to deduplicate leads → fails (field not marked as unique; duplicates allowed)
- Integrate with inventory system via VIN → fails (system expects constrained format)
- Report on leads by VIN → slow (no index; query performance terrible)
- Validate VIN format → can't (no format validation in field definition)

**Why it happens:**

Custom field model focuses on basic properties (name, type). Doesn't capture metadata:
- `IsUnique` — prevent duplicates
- `IsIndexed` — query performance
- `IsRequired` — validation by stage
- `Format` or `Regex` — data format validation
- `MinLength`, `MaxLength` — constraints
- `HelpText`, `Description` — user guidance

Recipe focuses on what fields exist, not how they should behave.

**Consequences:**

- Field doesn't behave as expected (VIN allows duplicates; should be unique)
- Performance issues (missing indexes on frequently-queried fields; reporting slow)
- Integration fails (external system expects VIN format AAAA-AAAA-AAAA-AAAA; system sends any text)
- User confusion (field description missing; user doesn't know what field means)
- Data quality issues (VIN values inconsistent: "VIN123", "vin123", "123", etc.)

**Prevention:**

1. **Extend custom field model to capture metadata**
   ```csharp
   public class CustomField
   {
     public Guid Id { get; set; }
     public string Name { get; set; }
     public string Type { get; set; }  // "Text", "Number", "Date", "Enum"
     public bool IsRequired { get; set; }

     // Metadata
     public bool IsUnique { get; set; }           // ← Add this
     public bool IsIndexed { get; set; }          // ← Add this
     public string Format { get; set; }           // "VIN" = 17 chars, "Phone" = (XXX) XXX-XXXX
     public string Regex { get; set; }            // Validation pattern
     public int? MinLength { get; set; }
     public int? MaxLength { get; set; }

     // UX
     public string HelpText { get; set; }         // ← Add this
     public string Description { get; set; }      // ← Add this
   }
   ```

2. **Include metadata in recipe definition**
   ```json
   {
     "fields": [
       {
         "name": "VIN",
         "type": "Text",
         "isRequired": true,
         "isUnique": true,
         "isIndexed": true,
         "format": "VIN",
         "minLength": 17,
         "maxLength": 17,
         "helpText": "Vehicle Identification Number — unique identifier",
         "description": "17-character code identifying vehicle"
       }
     ]
   }
   ```

3. **Apply metadata during seeding**
   ```csharp
   // Create actual DB constraint during seeding
   if (fieldSpec.IsUnique)
   {
     // ALTER TABLE leads ADD CONSTRAINT unique_vin UNIQUE (vin)
     context.Database.ExecuteSqlRaw(
       $"ALTER TABLE custom_field_values ADD CONSTRAINT unique_{fieldSpec.Name.ToLower()} " +
       $"UNIQUE (tenant_id, name) WHERE name = '{fieldSpec.Name}'");
   }

   if (fieldSpec.IsIndexed)
   {
     context.Database.ExecuteSqlRaw(
       $"CREATE INDEX idx_{fieldSpec.Name.ToLower()} ON custom_field_values (tenant_id, name, value)");
   }
   ```

4. **Test constraints and indexes**
   ```csharp
   [Fact]
   public void AutomobileRecipe_VIN_Field_IsUnique_Indexed()
   {
     var tenantDb = SetupTestTenantDb();
     ApplyRecipe("automobile-v1.0", tenantDb);

     // Query DB schema
     var schema = tenantDb.Database.GetDbConnection().GetSchema("Columns");
     var vinColumn = schema.Select(c => c["COLUMN_NAME"] == "VIN");

     // Check constraints
     var constraints = tenantDb.Database.GetDbConnection().GetSchema("Constraints");
     Assert.Contains(c => c["CONSTRAINT_TYPE"] == "UNIQUE" && c["COLUMN_NAME"] == "VIN", constraints);

     // Check indexes
     var indexes = tenantDb.Database.GetDbConnection().GetSchema("Indexes");
     Assert.Contains(i => i["COLUMN_NAME"] == "VIN" && i["UNIQUE"] == true, indexes);
   }
   ```

**Detection:**

- Query: `SELECT * FROM custom_fields WHERE is_indexed = false AND name = 'VIN'` — should be indexed
- Performance: VIN-based lookups slow; missing index evident in query plans
- Test: Insert lead with duplicate VIN; should fail if field unique

**Phase responsibility:** **v1.1 Recipe Foundation — Design** — Add metadata support to custom field model before seeding.

---

### Pitfall 8: Workflow Rules Don't Execute or Trigger Incorrectly After Seeding

**What goes wrong:**

Automobile recipe includes workflow rule:
```json
{
  "name": "Auto-Task on Proposal",
  "trigger": { "event": "LeadTransitionedToStage", "stageName": "Proposal" },
  "actions": [
    { "type": "CreateTask", "title": "Send Quote", "dueIn": "2d" }
  ],
  "isEnabled": true
}
```

Recipe applied. Tenant moves lead to "Proposal" stage. Expected: Task "Send Quote" created automatically. Actual: No task created.

Investigation:
- Rule seeded correctly in DB
- But Hangfire job never executed (no-op)
- Or workflow rule format wrong (JSON doesn't match rule engine schema)
- Or rule disabled by default (`isEnabled` not read during seeding)
- Or rule engine expects different field names ("stageName" vs "stage_name")

**Why it happens:**

Recipe includes workflow rules as JSON/config. Easy to:
- Get JSON format wrong (mismatch between recipe spec and rule engine expectations)
- Forget enabling rules (default `isEnabled = false`)
- Misname fields (rule engine expects "stageName"; recipe has "stage_name")
- Not validate rule before seeding (invalid rule seeded successfully, but fails at execution)

**Consequences:**

- Automation doesn't work out of the box (tenant thinks feature is broken)
- High support overhead (tenant has to manually configure rules that should work)
- Tenant loses key value (automation is major feature; if broken, trust drops)
- Subtle failures (rule executes but produces wrong task; tenant doesn't notice until too late)

**Prevention:**

1. **Validate recipe rule format before seeding**
   ```csharp
   var recipeSpec = RecipeDefinition.Load("automobile-v1.0.json");

   foreach (var ruleSpec in recipeSpec.WorkflowRules)
   {
     try
     {
       // Validate JSON against WorkflowRule schema
       var rule = JsonConvert.DeserializeObject<WorkflowRule>(ruleSpec);
       ValidateWorkflowRule(rule);  // Check triggers, actions, field names
     }
     catch (ValidationException ex)
     {
       throw new RecipeDefinitionException(
         $"Recipe workflow rule '{ruleSpec.Name}' is invalid: {ex.Message}");
     }
   }
   ```

2. **Enable rules by default (unless explicitly disabled)**
   ```csharp
   new WorkflowRule {
     Name = "Auto-Task on Proposal",
     IsEnabled = true,  // ← Default to enabled
     // ... trigger, actions
   }
   ```

3. **Test rule execution end-to-end**
   ```csharp
   [Fact]
   public void AutomobileRecipe_WorkflowRule_ExecutesWhenLeadMovesToProposal()
   {
     var tenantDb = SetupTestTenantDb();
     ApplyRecipe("automobile-v1.0", tenantDb);

     var lead = new Lead { Name = "Alice", Status = "New" };
     tenantDb.Leads.Add(lead);
     tenantDb.SaveChanges();

     // Move lead to Proposal
     lead.Status = "Proposal";
     tenantDb.SaveChanges();

     // Trigger workflow engine
     var workflowEngine = new WorkflowEngine(tenantDb);
     workflowEngine.ProcessLeadStateChange(lead.Id);

     // Verify task created
     var task = tenantDb.Tasks.FirstOrDefault(t => t.LeadId == lead.Id && t.Title == "Send Quote");
     Assert.NotNull(task);
   }
   ```

4. **Document rule format in recipe spec**
   ```
   WorkflowRule format:
   {
     "name": "string",
     "description": "string",
     "trigger": {
       "event": "string",  // "LeadTransitionedToStage", "LeadFieldChanged"
       "stageName": "string",  // For LeadTransitionedToStage
       "fieldName": "string"   // For LeadFieldChanged
     },
     "actions": [
       {
         "type": "string",  // "CreateTask", "SendEmail", "AssignToRole"
         "title": "string"
       }
     ],
     "isEnabled": boolean
   }
   ```

**Detection:**

- Test: Apply recipe with workflow rules; move lead through pipeline; verify tasks created
- Monitoring: Workflow rule created but never executed; log warning ("Rule X exists but no executions")
- Check: `SELECT COUNT(*) FROM workflow_rules WHERE is_enabled = false` — should be low
- Audit: Hangfire job logs show rule execution attempts and results

**Phase responsibility:** **v1.1 Recipe Definition** — Validate rule format; **v1.1+ Workflow Engine** — test rules execute after provisioning.

---

### Pitfall 9: Testing Complexity — Recipe-Dependent Provisioning Tests Are Slow and Flaky

**What goes wrong:**

To test any feature (e.g., "create lead"), test must:
1. Provision a tenant (takes 2-3 seconds)
2. Select industry recipe (1 second)
3. Apply recipe — seed stages, fields, rules, roles (2-3 seconds)
4. Then run actual test (0.5 seconds)
5. Total: 5-6 seconds per test

Test suite: 100 tests × 5-6 seconds = 500-600 seconds (8-10 minutes).

If recipe seeding is flaky (connection timeout, constraint violation), test is flaky. Developers don't run tests locally; CI is the test runner. Feedback loop slow.

**Why it happens:**

Recipe is applied during every test setup. No test fixture for pre-seeded tenant + recipe. Recipe data not stable (changes frequently; tests break).

**Consequences:**

- Test suite slow (10+ minutes; developers skip running locally)
- Tests flaky (recipe seeding sometimes fails; CI runs tests multiple times)
- Developer feedback loop slow (run tests, wait 10 minutes, fail, debug)
- Regression tests rarely run (developers skip if suite slow)
- CI/CD pipeline slow (tests block deployment)

**Prevention:**

1. **Create reusable test fixtures for pre-seeded recipes**
   ```csharp
   // Setup once, reuse across tests
   public class AutomobileRecipeFixture : IAsyncLifetime
   {
     private PostgreSqlContainer _container;
     public TenantDbContext DbContext { get; private set; }

     public async Task InitializeAsync()
     {
       _container = new PostgreSqlBuilder().Build();
       await _container.StartAsync();

       DbContext = new TenantDbContext(_container.GetConnectionString());
       await DbContext.Database.MigrateAsync();
       await ApplyRecipe("automobile-v1.0", DbContext);
     }

     public async Task DisposeAsync()
     {
       await _container.StopAsync();
     }
   }

   // Use in tests
   [Collection("AutomobileRecipeCollection")]
   public class LeadCreationTests : IClassFixture<AutomobileRecipeFixture>
   {
     private readonly AutomobileRecipeFixture _fixture;

     public LeadCreationTests(AutomobileRecipeFixture fixture)
     {
       _fixture = fixture;
     }

     [Fact]
     public void CreateLead_WithValidData_Succeeds()
     {
       var lead = new Lead { Name = "Alice", Status = "New" };
       _fixture.DbContext.Leads.Add(lead);
       _fixture.DbContext.SaveChanges();

       var savedLead = _fixture.DbContext.Leads.Find(lead.Id);
       Assert.NotNull(savedLead);
     }
   }
   ```

2. **Separate recipe validation tests from integration tests**
   ```
   Tests in RecipeProvisioningTests: "Recipe seeds correctly" (runs once)
   - Provision tenant
   - Apply Automobile recipe
   - Validate: 4 stages seeded, 10 fields seeded, 3 rules seeded

   Tests in FeatureTests: Assume recipe already exists
   - Don't provision; use fixture with pre-seeded recipe
   - Test: Can create lead? Can move lead through stages? etc.
   ```

3. **Mock recipe application in unit tests**
   ```csharp
   // Unit test: Don't seed recipe; mock it
   [Fact]
   public void WorkflowEngine_TriggersRuleWhenLeadMovesToProposal()
   {
     var mockRecipe = new Mock<IRecipeProvider>();
     mockRecipe.Setup(r => r.GetRules())
       .Returns(new[] { rule });

     var workflowEngine = new WorkflowEngine(mockRecipe.Object);
     workflowEngine.ProcessLeadStateChange(lead, "Proposal");

     // Verify rule triggered
   }
   ```

4. **Keep recipe test data stable**
   ```
   Don't change test recipe unless intentional.
   Version it: "automobile-v1.0-test.json"
   If you update recipe, update test data, run tests, commit both together.
   ```

5. **Measure and monitor test performance**
   ```
   CI log output:
   Test: CreateLead_WithValidData — 0.5s ✓
   Setup (provisioning): 5s (shows time spent in fixture)
   Total: 5.5s

   If setup > 2s, optimize:
   - Pre-seed multiple recipes in one fixture?
   - Cache provisioned DBs?
   - Parallelize test suites (separate fixtures for Automobile, Education)?
   ```

**Detection:**

- Measure test suite runtime — if > 5 minutes, provisioning likely culprit
- Check test logs — recipe seeding failures? Timeouts?
- Survey: Do developers run tests locally? (If not, provisioning likely too slow)

**Phase responsibility:** **v1.1 Recipe Foundation — Design for testability** — Build test fixtures early; **v1.1+ Integration Tests** — use fixtures, keep test suite fast.

---

### Pitfall 10: Recipe UI Display Doesn't Match Seeded Database Configuration

**What goes wrong:**

Recipe seeded stages: "Lead" → "Qualified" → "Proposal" → "Won". Blazor UI displays: "Lead" → "Qualified" → "Quote" → "Won" (different name, missing stage).

Causes:
- Recipe definition and UI display logic both have stage list; one updated, other not
- UI hardcoded stage names (for example, in component)
- Stage name changed in recipe; UI still shows old name
- Configuration not loaded from DB; hardcoded in UI logic

**Consequences:**

- Confusing UX (UI says "Quote", lead actually in "Proposal" state)
- Workflows break (rule triggers on "Proposal"; lead never reaches "Proposal" if it's "Quote" in UI)
- Trust issues (system appears inconsistent; tenant confused)
- Data quality (tenant doesn't understand actual state)

**Prevention:**

1. **Load all stage/field data from database; never hardcode in UI**
   ```csharp
   // Blazor component
   @code {
     private List<PipelineStage> Stages { get; set; }

     protected override async Task OnInitializedAsync()
     {
       // Load from DB, not hardcoded
       Stages = await http.GetFromJsonAsync<List<PipelineStage>>(
         "api/pipeline-stages");
     }
   }

   // Render stages from DB
   @foreach (var stage in Stages)
   {
     <div>@stage.Name</div>
   }
   ```

2. **Test UI against seeded recipe**
   ```csharp
   [Fact]
   public async Task KanbanBoard_DisplaysStagesFromDatabase()
   {
     var tenantDb = SetupTestTenantDb();
     ApplyRecipe("automobile-v1.0", tenantDb);

     var client = new BlazeClient(server);  // Test client for Blazor
     var page = await client.GoToAsync("/dashboard/kanban");

     var stageLabels = page.QuerySelectorAll(".kanban-stage").Select(s => s.TextContent);
     Assert.Contains("Lead", stageLabels);
     Assert.Contains("Qualified", stageLabels);
     Assert.Contains("Proposal", stageLabels);
     Assert.Contains("Won", stageLabels);
     Assert.DoesNotContain("Quote", stageLabels);  // ← Should not be there
   }
   ```

3. **Avoid duplication**
   - Recipe definition in one place (central DB or JSON file)
   - UI queries that source of truth (doesn't duplicate stage names)

**Detection:**

- Test: Provision with recipe; load UI; verify displayed stages match DB
- Visual regression: Stage list in UI vs DB schema (compare)
- Alerts: UI renders stage that doesn't exist in DB (error in console)

**Phase responsibility:** **v1.1 Blazor Frontend** — Bind UI to DB-loaded recipe data, not hardcoded values.

---

## Minor Pitfalls

Mistakes that cause friction but are easy to recover from.

---

### Pitfall 11: Recipe Image/Logo/Branding Not Included in Seeding

Automobile recipe should show a car icon in the recipe selector. But image URL not seeded → appears as broken link in UI.

**Prevention:** Include recipe metadata (image URL, description, color) in recipe definition; seed during provisioning.

**Phase:** v1.1 Recipe UI — Include images in recipe definition and display.

---

### Pitfall 12: Multiple Recipes Applied to Same Tenant (Accidental Re-Provisioning)

Tenant provisions with Automobile recipe. Later, admin accidentally re-runs provisioning with Education recipe. Now database has mixed stages/fields from both recipes.

**Prevention:** Tenant can't change recipe after initial selection. Provisioning validates: if tenant already has recipe, don't re-apply; offer upgrade path instead.

**Phase:** v1.1 Recipe Selection — One recipe per tenant, immutable at signup. Track which recipe applied.

---

### Pitfall 13: Recipe Definition Not Documented for Tenant

Tenant receives Automobile recipe but doesn't know:
- Why is "Service Date" required in Proposal stage?
- What does "Extended Warranty" field do?
- Which fields can be deleted?

**Prevention:** After provisioning, show tenant documentation: "Your Automobile workspace includes these fields and stages. Here's what each does."

**Phase:** v1.1 Recipe Guidance — Documentation in UI post-provisioning.

---

## Phase-Specific Warnings and Mitigation Roadmap

| Phase | Topic | Likely Pitfall | Prevention | Criticality |
|-------|-------|---------------|------------|------------|
| v1.1 Foundation | Recipe Data Model | Non-idempotent seeding; template vs instance confusion | Transactional seeding; clarify central (template) vs tenant (instance) ownership | **CRITICAL** |
| v1.1 Foundation | Recipe Seeding Code | Partial failures leave DB inconsistent | Wrap entire recipe in transaction; detailed error messages; logs | **CRITICAL** |
| v1.1 Automobile | Field Definitions | Wrong data types (text vs enum); missing constraints (unique, indexed) | Industry review; validate schema post-seeding | **HIGH** |
| v1.1 Education | Field Definitions | GPA field wrong type; missing format validation | Domain expert review; test constraints | **HIGH** |
| v1.1 Blank Template | Minimum Viability | User sees empty canvas; can't create lead | Provide minimum config or setup wizard | **HIGH** |
| v1.1 Recipe Selection | One Recipe Per Tenant | Accidental re-provision with different recipe | Validate: recipe already applied; block re-provisioning | **HIGH** |
| v1.1 Workflow Rules | Rule Execution | Rules don't trigger after seeding | Validate rule format; enable by default; test end-to-end | **HIGH** |
| v1.1 Blazor Frontend | UI Configuration Display | Hardcoded stage names don't match DB | Load all stages/fields from DB; test UI matches DB | **HIGH** |
| v1.1 Testing | Test Performance | Recipe seeding slows all tests | Build reusable fixtures; separate recipe tests from feature tests | **MEDIUM** |
| v1.1 Field Metadata | Rich Field Configuration | Missing unique/indexed constraints; no validation format | Extend field model; include metadata in recipe; apply constraints | **MEDIUM** |
| v1.1 Documentation | User Onboarding | Tenant doesn't understand recipe; deletes critical field | Document recipe intent; warn before deleting critical fields | **MEDIUM** |
| v1.2+ Recipe Updates | Version Tracking & Migration | Existing tenants drift; can't update recipe | Implement recipe versioning; design for migration tooling | **MEDIUM** |
| v1.2+ Recipe Improvements | Backward Compatibility | Breaking recipe changes affect existing tenants | Make updates additive; provide upgrade wizard; changelog | **MEDIUM** |

---

## Key Takeaways for Roadmap

1. **v1.1 Recipe Foundation must get seeding idempotency right** — Transactional, with existence checks and detailed error messages. This is the foundation everything else depends on. If seeding isn't robust, all other work is at risk.

2. **Clarify recipe ownership early** — Templates in central DB (immutable, applied at provisioning), instances in tenant DB (mutable, customizable). Design schema and code accordingly. Avoid confusion later.

3. **Industry review before release** — Automobile and Education recipes must match real-world workflows and data models. Get feedback from 2-3 practitioners before seeding. Don't guess at field types or stages.

4. **Test end-to-end provisioning** — Provision with recipe → DB seeded correctly → UI displays correctly → workflow rules execute. All three must work together. Integration tests are essential.

5. **Provide recipe documentation and guidance** — After provisioning, tenant should understand what recipe they got, which fields are critical, which are customizable. Reduce support burden.

6. **Build test fixtures early** — Recipe-dependent provisioning tests will slow down development. Invest in reusable fixtures to keep test suite < 5 minutes.

7. **Plan for recipe updates** — Even though v1.1 treats recipes as static, design the data model to support versioning and upgrades in v1.2+. Track recipe version per tenant; enable recipe upgrade workflows.

---

## Sources

- [Qrvey: Multi-Tenant Deployment — 2026 Complete Guide](https://qrvey.com/blog/multi-tenant-deployment/)
- [WorkOS: The Developer's Guide to SaaS Multi-Tenant Architecture](https://workos.com/blog/developers-guide-saas-multi-tenant-architecture)
- [Sachith Dassanayake: Multi-tenant SaaS Data Isolation — Scaling Strategies (March 2026)](https://www.sachith.co.uk/multi%E2%80%91tenant-saas-data-isolation-scaling-strategies-practical-guide-mar-23-2026/)
- [Microsoft Azure: Multitenant SaaS Patterns](https://learn.microsoft.com/en-us/azure/azure-sql/database/saas-tenancy-app-design-patterns?view=azuresql)
- [Atlas: Database-per-Tenant Architecture](https://atlasgo.io/use-cases/database-per-tenant)
- [Code with Mukesh: Seeding Initial Data in EF Core 10](https://codewithmukesh.com/blog/seeding-initial-data-efcore/)
- [Microsoft: Data Seeding — EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [Microsoft: Multi-Tenancy — EF Core](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [CRM Masters: CRM Sales Forecasting — Predict Revenue Using Pipeline Data](https://crm-masters.com/crm-sales-forecasting-how-to-predict-revenue-using-pipeline-data/)
- [Digital Scouts: Common HubSpot Integration Mistakes](https://digitalscouts.co/blog/common-hubspot-integration-mistakes-and-how-to-avoid-them)
- [Expert VA: CRM Stage Management Mistakes](https://expertva.com/post/11-CRM-Stage-Management-Mistakes)
- [Twilio: Prevent Race Conditions in Laravel with Atomic Locks](https://www.twilio.com/en-us/blog/developers/tutorials/prevent-race-conditions-laravel-atomic-locks)
- [TestGrid: Multi-Tenancy Testing — What Is It and How Does It Work](https://testgrid.io/blog/multi-tenancy/)
- [QASource: What is Multi-Tenant Database Architecture? How To Test It?](https://blog.qasource.com/multi-tenant-database-architecture-and-how-to-test-it)
- [AWS CloudFormation: Handle Failures When Provisioning Resources](https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/stack-failure-options.html)
- [iTransition: CRM for the Automotive Industry](https://www.itransition.com/crm/automotive)
- [Salesforce: Automotive CRM — A Complete Guide](https://www.salesforce.com/automotive/crm-guide/)
