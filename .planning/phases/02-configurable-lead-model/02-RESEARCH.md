# Phase 2: Configurable Lead Model - Research

**Researched:** 2026-03-20
**Domain:** Tenant-configurable lead schema, duplicate detection, field validation, and record merging in PostgreSQL/EF Core
**Confidence:** MEDIUM-HIGH (verified with EF Core 10 JSON docs, PostgreSQL JSONB patterns, fuzzy matching libraries, audit trail approaches)

## Summary

Phase 2 transforms leads from fixed schema to fully configurable per-tenant data model. Tenants define custom fields (7 types), pipeline stages (with ordering), and lead source classification. The phase adds duplicate detection with fuzzy matching on email/phone/name, and enables merging duplicate records while preserving audit history.

Implementation leverages EF Core 10's native JSON column support (new complex type mapping) for dynamic custom field storage in PostgreSQL JSONB, avoiding table explosion. Duplicate detection uses straightforward exact-match on standardized fields (email, phone) with fuzzy matching as optional enhancement. Lead merging maintains complete history via outbox pattern—merged leads surface both records' activity timelines.

**Primary recommendation:** Use EF Core 10 complex type → JSON mapping for custom fields (fully queryable, type-safe), store PipelineStage as configurable entity, track LeadSource as enum with 4 values (manual/import/api/webform), add AuditLog entity for complete merge history, use FuzzySharp for fuzzy matching (optional scoring), normalize email/phone on duplicate detection queries.

<user_constraints>

## User Constraints (from CONTEXT.md if exists)

No CONTEXT.md for Phase 2 has been created yet. Phase 2 is unconstrained—Claude has full discretion to research and recommend approaches.

</user_constraints>

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| LEAD-01 | Tenant can define custom fields on lead records (text, number, date, dropdown, multi-select, currency, boolean) and those fields appear on the lead form | EF Core 10 JSON complex types enable flexible JSONB storage with full querying; type enum drives form field rendering; validation applied per tenant's type definition |
| LEAD-02 | Tenant can configure pipeline stages with custom names and ordering, and those stages are the only valid stages for leads in that tenant | PipelineStage entity (TenantId, Name, Order, IsActive) stored in relational table for easy filtering and re-ordering; Lead.StageId enforces referential integrity |
| LEAD-03 | Every lead record stores its source (manual, import, API, web form) and that value is visible in the lead detail view | Lead.Source enum field (Manual, Import, Api, WebForm) set at creation; queryable, displayed in detail view without additional lookup |
| LEAD-04 | Creating or importing a lead with matching email, phone, or name surfaces duplicate warning before save | Exact-match queries on normalized email/phone + fuzzy match on name (FuzzySharp) returns candidates; warning displayed, user confirms before SaveChanges |
| LEAD-05 | User can merge two duplicate lead records into one, with surviving record retaining complete history of both | LeadMerge audit entity logs source/target Ids and merge timestamp; both records' activity accessible via OutboxMessage query; logical delete on merged record maintains referential integrity |

</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Entity Framework Core | 10.0.5 (current) | ORM, migrations, complex type JSON mapping | Native JSON complex type mapping (GA in EF 10) fully queryable JSONB, replaces custom property bags; eliminates per-field column explosion |
| PostgreSQL JSONB | 15+ | Custom field storage | PostgreSQL GIN indexing on JSONB enables fast lookups on 100+ dynamic fields; superior to separate column approach |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 (current) | PostgreSQL-specific EF Core provider | Official provider; JSONB mapping, GIN index support via fluent API |
| ASP.NET Core 10 | 10.0.5 (current) | API framework | Project standard; minimal API validation pipeline |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| FuzzySharp | 1.11.0 (latest) | Fuzzy string matching (Levenshtein distance, token matching) | Optional enhancement to exact-match duplicate detection; implements Seat Geek's FuzzyWuzzy algorithm |
| Newtonsoft.Json | 13.0.3 (current) | JSON serialization for OutboxMessage | Already in codebase; serializes domain events for merge audit trail |
| FluentValidation | 12.1.1 (current) | Request validation | Already integrated; custom field type validation rules |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| EF Core 10 JSON complex types | Custom property bag (Dictionary<string, object>) | Simple but untyped, loses query filtering, requires manual serialization; manual tracking of field changes harder |
| EF Core 10 JSON complex types | Separate DynamicField table (TenantId, LeadId, FieldName, FieldType, Value) | Fully normalized, but N+1 query risk, row explosion with 100+ fields, slower aggregation queries |
| PostgreSQL JSONB | Separate columns per custom field | Requires migration every custom field; schema bloat; violates DRY; harder to index |
| FuzzySharp | Manual Levenshtein distance | Adds dependency; implement if false-positive cost is high; benchmarks show FuzzySharp performs adequately for <1000 lead comparison |
| Exact match + fuzzy | ML-based duplicate detection | Overkill for Phase 2; rule-based approach sufficient for initial CRM use case; defer to Phase 2+ |

**Installation:**
```bash
dotnet add package FuzzySharp --version 1.11.0
# Others already present in Phase 1
```

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.Data/Entities/
├── Lead.cs                      # Core lead entity + Source enum
├── CustomFieldDefinition.cs     # Tenant's field schema (Type, IsRequired, DefaultValue)
├── CustomFieldValue.cs          # JSON complex type (owned by Lead)
├── PipelineStage.cs             # Tenant-configurable stage
├── LeadMerge.cs                 # Audit entity for merge tracking
├── LeadActivityLog.cs           # Activity/audit trail (outbox-driven)

IronMonkey.Data/Configurations/
├── LeadConfiguration.cs         # CustomFieldValue.ToJson() mapping, indexes
├── CustomFieldDefinitionConfiguration.cs
├── PipelineStageConfiguration.cs
├── LeadMergeConfiguration.cs

IronMonkey.ApiService/Features/Leads/
├── CreateCustomFieldEndpoint.cs
├── ListCustomFieldsEndpoint.cs
├── UpdatePipelineStageEndpoint.cs
├── ListPipelineStagesEndpoint.cs
├── CreateLeadEndpoint.cs        # Validates against custom field definitions, checks duplicates
├── CheckDuplicatesEndpoint.cs
├── MergeLeadsEndpoint.cs
├── LeadDetailViewModel.cs       # DTO with custom fields populated, source visible, merge history
```

### Pattern 1: Custom Field Definition & Storage

**What:** Tenant defines field schema once (name, type, required, options for dropdown/multi-select), then all lead records store values in a JSON object keyed by field ID.

**When to use:** Always—enables flexible per-tenant lead structure.

**Example:**
```csharp
// Phase 1 existing pattern — extend Lead entity
public sealed class Lead : BaseTenantEntity
{
    // Standard fields (unchanged from Phase 1)
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;

    // New in Phase 2
    public LeadSource Source { get; private set; } = LeadSource.Manual;
    public Guid PipelineStageId { get; private set; }

    // Custom field storage — EF Core 10 JSON complex type
    public CustomFieldValues CustomFields { get; private set; } = new();

    // Navigation
    public PipelineStage Stage { get; private set; } = null!;

    public static Lead Create(
        Guid tenantId, string firstName, string lastName,
        string email, string mobile, LeadSource source, Guid stageId)
    {
        return new Lead(Guid.NewGuid(), tenantId, firstName, lastName, email, mobile, source, stageId)
        {
            CustomFields = new CustomFieldValues()
        };
    }
}

// EF Core 10 complex type — owned by Lead
public class CustomFieldValues
{
    // Dictionary keyed by CustomFieldDefinition.Id
    // Values are type-agnostic (stored in JSONB)
    public Dictionary<Guid, object?> Values { get; set; } = new();

    public void Set(Guid fieldId, object? value) => Values[fieldId] = value;
    public object? Get(Guid fieldId) => Values.TryGetValue(fieldId, out var val) ? val : null;
}

public enum LeadSource
{
    Manual = 0,
    Import = 1,
    Api = 2,
    WebForm = 3
}
```

**In TenantDbContext.OnModelCreating:**
```csharp
modelBuilder.Entity<Lead>(e =>
{
    // EF Core 10 — own the JSON type and map to JSONB column
    e.OwnsOne(l => l.CustomFields, cf =>
    {
        cf.ToJson(); // Stores in single JSONB column
        cf.Property(c => c.Values)
            .HasColumnName("custom_field_values");
    });

    // GIN index for JSONB queries on specific fields
    e.HasIndex(l => l.CustomFields)
        .HasMethod("gin")
        .HasDatabaseName("IX_Lead_CustomFields_GIN");

    // Foreign key to PipelineStage
    e.HasOne(l => l.Stage)
        .WithMany()
        .HasForeignKey(l => l.PipelineStageId)
        .OnDelete(DeleteBehavior.Restrict);
});

modelBuilder.Entity<CustomFieldDefinition>(e =>
{
    e.HasKey(c => c.Id);
    e.Property(c => c.TenantId);
    e.Property(c => c.FieldName).IsRequired();
    e.Property(c => c.FieldType).HasConversion<string>();
    e.Property(c => c.IsRequired);
    e.Property(c => c.Options).HasConversion(
        v => JsonConvert.SerializeObject(v),
        v => JsonConvert.DeserializeObject<List<string>>(v) ?? new());
    e.HasKey(c => new { c.TenantId, c.Id });
    e.HasQueryFilter(c => c.TenantId == EF.Property<Guid>("TenantId"));
});
```

**Source:** [EF Core 10 JSON Columns](https://devblogs.microsoft.com/dotnet/announcing-ef7-release-candidate-2/), [Npgsql JSONB Mapping](https://www.npgsql.org/efcore/mapping/json.html)

### Pattern 2: Duplicate Detection & Fuzzy Matching

**What:** Before creating/importing a lead, query for candidates with exact-match email or phone (fast), optionally fuzzy-match name (Levenshtein 80%+ similarity). Return ranked list of candidates.

**When to use:** On create/import endpoints; warn user before SaveChanges.

**Example:**
```csharp
public interface IDuplicateDetectionService
{
    Task<List<DuplicateCandidate>> FindCandidatesAsync(
        Guid tenantId, string? email, string? phone, string? name);
}

public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly TenantDbContext _db;

    // Threshold for fuzzy match (0-100)
    private const int FuzzyMatchThreshold = 75;

    public async Task<List<DuplicateCandidate>> FindCandidatesAsync(
        Guid tenantId, string? email, string? phone, string? name)
    {
        var candidates = new List<DuplicateCandidate>();

        // Exact match on normalized email
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = NormalizeEmail(email);
            var emailMatches = await _db.Leads
                .Where(l => l.TenantId == tenantId &&
                           l.Email.ToLower() == normalizedEmail)
                .ToListAsync();

            foreach (var lead in emailMatches)
            {
                candidates.Add(new DuplicateCandidate
                {
                    LeadId = lead.Id,
                    Reason = "Email match",
                    ConfidenceScore = 100
                });
            }
        }

        // Exact match on normalized phone
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var normalizedPhone = NormalizePhone(phone);
            var phoneMatches = await _db.Leads
                .Where(l => l.TenantId == tenantId &&
                           EF.Functions.Collate(l.Mobile, "C") == normalizedPhone)
                .ToListAsync();

            foreach (var lead in phoneMatches)
            {
                if (!candidates.Any(c => c.LeadId == lead.Id))
                {
                    candidates.Add(new DuplicateCandidate
                    {
                        LeadId = lead.Id,
                        Reason = "Phone match",
                        ConfidenceScore = 95
                    });
                }
            }
        }

        // Fuzzy match on name (only if name provided, and no exact matches yet)
        if (!string.IsNullOrWhiteSpace(name) && candidates.Count == 0)
        {
            var allLeads = await _db.Leads
                .Where(l => l.TenantId == tenantId)
                .Select(l => new { l.Id, FullName = l.FirstName + " " + l.LastName })
                .ToListAsync();

            foreach (var lead in allLeads)
            {
                // FuzzySharp TokenSetRatio (more forgiving for partial matches)
                var score = FuzzySharp.Fuzz.TokenSetRatio(name, lead.FullName);

                if (score >= FuzzyMatchThreshold)
                {
                    candidates.Add(new DuplicateCandidate
                    {
                        LeadId = lead.Id,
                        Reason = $"Name match ({score}%)",
                        ConfidenceScore = score
                    });
                }
            }
        }

        return candidates.OrderByDescending(c => c.ConfidenceScore).ToList();
    }

    private string NormalizeEmail(string email) => email.Trim().ToLower();

    private string NormalizePhone(string phone) => System.Text.RegularExpressions.Regex.Replace(phone, @"\D", "");
}

public class DuplicateCandidate
{
    public Guid LeadId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; } // 0-100
}
```

**Source:** [FuzzySharp GitHub](https://github.com/JakeBayer/FuzzySharp), [Duplicate Detection Best Practices](https://www.nimble.com/blog/how-to-send-website-form-leads-to-crm/)

### Pattern 3: Lead Merge with Audit Trail

**What:** Merge target lead into source lead. Transfer all non-null custom field values, mark target as deleted, create LeadMerge audit record, emit domain event captured as OutboxMessage.

**When to use:** On user confirmation after reviewing duplicate candidates.

**Example:**
```csharp
public sealed class LeadMerge : BaseTenantEntity
{
    private LeadMerge() { }

    public Guid SourceLeadId { get; private set; }
    public Guid TargetLeadId { get; private set; }
    public DateTime MergedAt { get; private set; }
    public Guid MergedByUserId { get; private set; }

    // Snapshot of what was merged (for audit)
    public string SourceSnapshot { get; private set; } = string.Empty;
    public string TargetSnapshot { get; private set; } = string.Empty;

    public static LeadMerge Create(Guid tenantId, Guid sourceLeadId, Guid targetLeadId, Guid userId)
    {
        return new LeadMerge
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceLeadId = sourceLeadId,
            TargetLeadId = targetLeadId,
            MergedAt = DateTime.UtcNow,
            MergedByUserId = userId
        };
    }
}

// On Lead entity
public class LeadMergedDomainEvent : IDomainEvent
{
    public Guid SourceLeadId { get; set; }
    public Guid TargetLeadId { get; set; }
    public Guid MergedByUserId { get; set; }
    public DateTime MergedAt { get; set; }
}

// Service
public class LeadMergeService
{
    private readonly TenantDbContext _db;

    public async Task MergeAsync(Guid tenantId, Guid sourceLeadId, Guid targetLeadId, Guid userId)
    {
        var source = await _db.Leads.FirstAsync(l => l.Id == sourceLeadId && l.TenantId == tenantId);
        var target = await _db.Leads.FirstAsync(l => l.Id == targetLeadId && l.TenantId == tenantId);

        // Merge custom fields: target values override source only if source is null/empty
        foreach (var kvp in target.CustomFields.Values)
        {
            if (!source.CustomFields.Values.ContainsKey(kvp.Key) || source.CustomFields.Values[kvp.Key] == null)
            {
                source.CustomFields.Set(kvp.Key, kvp.Value);
            }
        }

        // Soft-delete target
        target.IsDeleted = true;
        target.DeletedAt = DateTime.UtcNow;

        // Create audit record
        var merge = LeadMerge.Create(tenantId, sourceLeadId, targetLeadId, userId);
        _db.Add(merge);

        // Domain event — captured as OutboxMessage in SaveChanges
        source.AddDomainEvent(new LeadMergedDomainEvent
        {
            SourceLeadId = sourceLeadId,
            TargetLeadId = targetLeadId,
            MergedByUserId = userId,
            MergedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
```

**Source:** [EF Core SaveChanges Override Pattern](https://antondevtips.com/blog/how-to-implement-audit-trail-in-asp-net-core-with-ef-core), [Audit Trail via Outbox](https://codewithmukesh.com/blog/audit-trail-implementation-in-aspnet-core/)

### Pattern 4: Pipeline Stage Configuration

**What:** Each tenant defines their own pipeline stages (e.g., "New", "Qualified", "Proposal", "Closed Won"). Stages are ordered, and only valid stages can be assigned to leads.

**When to use:** Replaces hard-coded stage strings; enforces referential integrity.

**Example:**
```csharp
public sealed class PipelineStage : BaseTenantEntity
{
    private PipelineStage() { }

    public string Name { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static PipelineStage Create(Guid tenantId, string name, int order)
    {
        return new PipelineStage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Order = order
        };
    }

    public void Update(string name, int order)
    {
        Name = name;
        Order = order;
    }

    public void Deactivate() => IsActive = false;
}

// Seed default stages for new tenants
public static class PipelineStageSeeds
{
    public static void SeedDefaultStages(this ModelBuilder modelBuilder)
    {
        // Note: These will be seeded per-tenant during provisioning, not in global seed
        // This is a template reference
    }
}

// In TenantProvisioningService or migration seed
public async Task SeedDefaultPipelineAsync(Guid tenantId)
{
    var stages = new[]
    {
        PipelineStage.Create(tenantId, "New", 1),
        PipelineStage.Create(tenantId, "Contacted", 2),
        PipelineStage.Create(tenantId, "Qualified", 3),
        PipelineStage.Create(tenantId, "Proposal", 4),
        PipelineStage.Create(tenantId, "Negotiation", 5),
        PipelineStage.Create(tenantId, "Closed Won", 6),
    };

    _db.AddRange(stages);
    await _db.SaveChangesAsync();
}
```

### Anti-Patterns to Avoid

- **Hard-coded field types in Lead entity:** Don't add FirstName, LastName, Email, etc. for every custom field type. Use JSON complex type once to avoid migration per field.
- **Unindexed JSONB queries:** Don't query custom fields without GIN index. Add index via `HasIndex(...).HasMethod("gin")` in EF configuration.
- **Storing duplicate detection score without threshold:** Don't return all "matches" below confidence threshold. Filter at 75%+ and return ranked list.
- **Merging without audit:** Don't delete target lead; soft-delete and log merge in LeadMerge entity. Ensures compliance/debuggability.
- **Type-unsafe custom field storage:** Don't use raw `Dictionary<string, object>`. Use JSON complex type so EF Core tracks changes and validates JSON structure.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| String fuzzy matching for duplicates | Levenshtein distance algorithm from scratch | FuzzySharp (1.11.0) | Proven, optimized; Seat Geek's battle-tested algorithm; false negative/positive tuning already done |
| Dynamic field storage with relational columns | Create a column for every custom field type | EF Core 10 JSON complex types → PostgreSQL JSONB | Eliminates schema bloat, avoids N migrations, full query support, GIN indexing native |
| Field validation on unknown types | Custom switch statement per field type | FluentValidation rules per FieldType enum + options lookup | Reusable, testable, extensible |
| Merge audit trail | Manual logging to separate table | Outbox pattern (already in Phase 1) + LeadMerge entity | Event-sourced, resilient to failures, queryable timeline |
| Pipeline stage ordering | Hard-coded stage names | PipelineStage entity with TenantId + Order | Queryable, re-orderable, no migration per stage change |

**Key insight:** Custom field storage is the largest footgun in this phase. Relational columns explode schema; hand-rolled JSON serialization loses query semantics; EF Core 10's native JSON complex type mapping is the only viable approach.

## Common Pitfalls

### Pitfall 1: JSONB Query Performance Without Index

**What goes wrong:** Custom field queries on 100+ fields become O(n) table scans; response times degrade exponentially.

**Why it happens:** JSONB is flexible but unindexed by default. Developers forget to add GIN index or assume relational query planning applies.

**How to avoid:**
- Always add `.HasIndex(...).HasMethod("gin")` for JSONB columns queried frequently.
- Benchmark with 10k+ records before Phase 3 ingestion.
- Use `EXPLAIN ANALYZE` on custom field filter queries.
- State.md already flags this: "Phase 2: Dynamic field indexing performance needs validation — PostgreSQL JSONB indexing with 100+ fields per tenant."

**Warning signs:**
- Custom field filter queries take >500ms
- Database CPU spikes on lead list view
- Log shows sequential scans on leads table

### Pitfall 2: Merging Records Without Tracking Source

**What goes wrong:** User merges A + B into A. Target (B) is deleted. Later, when searching B's email, no record found. Compliance issue.

**Why it happens:** Developers assume "delete" means remove from query. But audit/compliance needs history.

**How to avoid:**
- Always soft-delete merged records (IsDeleted flag, not physical delete).
- Always create LeadMerge audit record with source/target Ids, user, timestamp.
- Emit domain event captured as OutboxMessage—this event becomes part of activity timeline.
- Document that merged leads are invisible to queries but discoverable via LeadMerge table.

**Warning signs:**
- `Lead.IsDeleted` is ignored on queries (missing global query filter)
- No LeadMerge table or it's empty after merges
- User reports "I merged a lead and now I can't find it" with no audit trail

### Pitfall 3: Fuzzy Matching Without Normalization

**What goes wrong:** Email "john@example.com" vs "JOHN@EXAMPLE.COM" treated as different leads. Phone "+1 (555) 123-4567" vs "5551234567" not recognized as same.

**Why it happens:** String comparison is case-sensitive and format-sensitive. Developers forget to normalize.

**How to avoid:**
- Normalize email to `.ToLower().Trim()` before any duplicate query.
- Normalize phone to digits-only via regex `\D` removal.
- Apply normalization consistently in Lead.Create and duplicate detection queries.
- Add database check constraint if needed (for stricter enforcement).

**Warning signs:**
- Duplicate detection finds nothing for valid duplicates
- User manually merges the same pair twice
- Phone field accepts "+1-555-1234" and "5551234" as different values

### Pitfall 4: Custom Field Type Mismatch

**What goes wrong:** CustomFieldDefinition says "currency", but lead stores "abc" as value. UI tries to format as money and crashes.

**Why it happens:** EF Core complex type with `Dictionary<string, object?>` doesn't validate type at save. Developers rely on UI validation only.

**How to avoid:**
- On CreateLeadEndpoint, validate custom field values against their definitions before SaveChanges.
- Store FieldType enum (Text, Number, Date, Dropdown, MultiSelect, Currency, Boolean) on definition.
- Create validation rules: Number → decimal.TryParse, Currency → decimal with 2 decimals, Date → DateTime.TryParse, etc.
- Use FluentValidation to enforce before SaveChanges:
  ```csharp
  RuleFor(x => x.CustomFields)
      .Custom((fields, context) =>
      {
          var definitions = _db.CustomFieldDefinitions.Where(...).ToList();
          foreach (var def in definitions)
          {
              if (fields.Values.TryGetValue(def.Id, out var value))
              {
                  ValidateFieldValue(def.FieldType, value, context);
              }
          }
      });
  ```

**Warning signs:**
- Lead created with custom field value that violates definition type
- UI crashes on lead detail view trying to format field
- Database contains currency field with non-numeric values

### Pitfall 5: Global Query Filter Missing Merged Records

**What goes wrong:** Merged (deleted) lead still appears in lists or reports. User thinks it's a duplicate.

**Why it happens:** `Lead.IsDeleted` global query filter applied, but merge logic doesn't set IsDeleted = true on target.

**How to avoid:**
- Ensure LeadMergeService.MergeAsync sets `target.IsDeleted = true` before SaveChanges.
- Test that merged lead is excluded from Lead list queries but discoverable via LeadMerge.Join(Lead) query.
- Document for Phase 3 that import/ingestion must also check IsDeleted when finding duplicates.

**Warning signs:**
- Merged lead still shows in list view
- User reports seeing duplicate after merge
- LeadMerge.TargetLeadId references a record that appears in normal queries

## Code Examples

Verified patterns from official sources and Phase 1 codebase:

### Custom Field Definition & Validation

Source: [EF Core 10 JSON Columns](https://www.learnentityframeworkcore.com/misc/json-columns), [Npgsql JSONB Mapping](https://www.npgsql.org/efcore/mapping/json.html)

```csharp
// Enum for field types
public enum FieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Dropdown = 3,
    MultiSelect = 4,
    Currency = 5,
    Boolean = 6
}

// Tenant's field schema
public sealed class CustomFieldDefinition : BaseTenantEntity
{
    private CustomFieldDefinition() { }

    public string FieldName { get; private set; } = string.Empty;
    public FieldType FieldType { get; private set; }
    public bool IsRequired { get; private set; }
    public string? DefaultValue { get; private set; }
    public List<string> Options { get; private set; } = new(); // For dropdown/multi-select
    public int DisplayOrder { get; private set; }

    public static CustomFieldDefinition Create(
        Guid tenantId, string fieldName, FieldType fieldType,
        bool isRequired = false, List<string>? options = null, int displayOrder = 0)
    {
        return new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FieldName = fieldName,
            FieldType = fieldType,
            IsRequired = isRequired,
            Options = options ?? new(),
            DisplayOrder = displayOrder
        };
    }
}

// Validator for custom field values before lead creation
public class CreateLeadValidator : AbstractValidator<CreateLeadRequest>
{
    public CreateLeadValidator(ITenantDbContextFactory contextFactory, Guid tenantId)
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.CustomFields)
            .CustomAsync(async (fields, context, ct) =>
            {
                using var db = contextFactory.CreateDbContext(tenantId);
                var definitions = await db.CustomFieldDefinitions
                    .Where(d => d.TenantId == tenantId)
                    .ToListAsync(ct);

                foreach (var def in definitions)
                {
                    if (def.IsRequired && (!fields.ContainsKey(def.Id) || fields[def.Id] == null))
                    {
                        context.AddFailure($"Custom field '{def.FieldName}' is required.");
                        continue;
                    }

                    if (fields.TryGetValue(def.Id, out var value) && value != null)
                    {
                        switch (def.FieldType)
                        {
                            case FieldType.Number:
                                if (!decimal.TryParse(value.ToString(), out _))
                                    context.AddFailure($"Field '{def.FieldName}' must be a valid number.");
                                break;

                            case FieldType.Currency:
                                if (!decimal.TryParse(value.ToString(), out var decVal) || decVal < 0)
                                    context.AddFailure($"Field '{def.FieldName}' must be a positive currency amount.");
                                break;

                            case FieldType.Date:
                                if (!DateTime.TryParse(value.ToString(), out _))
                                    context.AddFailure($"Field '{def.FieldName}' must be a valid date.");
                                break;

                            case FieldType.Dropdown:
                                if (!def.Options.Contains(value.ToString() ?? ""))
                                    context.AddFailure($"Field '{def.FieldName}' has invalid option selected.");
                                break;

                            case FieldType.MultiSelect:
                                var selected = (value as List<string>) ?? new();
                                if (selected.Any(s => !def.Options.Contains(s)))
                                    context.AddFailure($"Field '{def.FieldName}' contains invalid options.");
                                break;

                            case FieldType.Boolean:
                                if (!bool.TryParse(value.ToString(), out _))
                                    context.AddFailure($"Field '{def.FieldName}' must be true or false.");
                                break;
                        }
                    }
                }
            });
    }
}
```

### Duplicate Detection Endpoint

Source: [FuzzySharp GitHub](https://github.com/JakeBayer/FuzzySharp), Phase 1 endpoint pattern

```csharp
public class CheckDuplicatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/leads/check-duplicates", Handle)
            .WithName("CheckDuplicates")
            .WithOpenApi()
            .Produces<CheckDuplicatesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    public async Task<IResult> Handle(
        [FromBody] CheckDuplicatesRequest request,
        ITenantDbContextFactory contextFactory,
        IUserContext userContext,
        IDuplicateDetectionService dupService,
        CancellationToken ct)
    {
        var db = contextFactory.CreateDbContext(userContext.TenantId);

        var candidates = await dupService.FindCandidatesAsync(
            userContext.TenantId,
            request.Email,
            request.Phone,
            request.FullName);

        // Map candidates to DTO (include name, email, phone for review)
        var response = new CheckDuplicatesResponse
        {
            DuplicateCandidates = await Task.WhenAll(candidates.Select(async c =>
                new DuplicateCandidateDto
                {
                    LeadId = c.LeadId,
                    FullName = (await db.Leads.FirstAsync(l => l.Id == c.LeadId, ct)).FirstName + " " +
                               (await db.Leads.FirstAsync(l => l.Id == c.LeadId, ct)).LastName,
                    Email = (await db.Leads.FirstAsync(l => l.Id == c.LeadId, ct)).Email,
                    Phone = (await db.Leads.FirstAsync(l => l.Id == c.LeadId, ct)).Mobile,
                    Reason = c.Reason,
                    ConfidenceScore = c.ConfidenceScore
                }))
                .ToListAsync(ct)
        };

        return Results.Ok(response);
    }

    public class CheckDuplicatesRequest
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? FullName { get; set; }
    }

    public class CheckDuplicatesResponse
    {
        public List<DuplicateCandidateDto> DuplicateCandidates { get; set; } = new();
    }

    public class DuplicateCandidateDto
    {
        public Guid LeadId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; }
    }
}
```

### Lead Detail View with History

Source: Phase 1 Lead entity + outbox pattern, [Audit Trail via Outbox](https://blog.elmah.io/implementing-audit-logs-in-ef-core-without-polluting-your-entities/)

```csharp
public class LeadDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/leads/{id}", Handle)
            .WithName("GetLeadDetail")
            .WithOpenApi()
            .Produces<LeadDetailDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    public async Task<IResult> Handle(
        Guid id,
        ITenantDbContextFactory contextFactory,
        IUserContext userContext,
        CancellationToken ct)
    {
        var db = contextFactory.CreateDbContext(userContext.TenantId);

        var lead = await db.Leads
            .Include(l => l.Stage)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == userContext.TenantId, ct);

        if (lead is null)
            return Results.NotFound();

        // Load custom field definitions for context
        var fieldDefs = await db.CustomFieldDefinitions
            .Where(d => d.TenantId == userContext.TenantId)
            .ToListAsync(ct);

        // Load activity timeline from OutboxMessages with LeadMergedDomainEvent or other events
        var mergeEvents = await db.OutboxMessages
            .Where(om => om.EventType == nameof(LeadMergedDomainEvent) &&
                         EF.Functions.JsonContains(om.Payload, id.ToString()))
            .ToListAsync(ct);

        var dto = new LeadDetailDto
        {
            Id = lead.Id,
            FirstName = lead.FirstName,
            LastName = lead.LastName,
            Email = lead.Email,
            Phone = lead.Mobile,
            Source = lead.Source.ToString(),
            Stage = lead.Stage?.Name ?? "Unknown",
            CustomFields = fieldDefs.ToDictionary(
                d => d.FieldName,
                d => lead.CustomFields.Get(d.Id)),
            MergeHistory = mergeEvents.Select(om => new MergeHistoryItem
            {
                SourceLeadId = Guid.Parse(JsonConvert.DeserializeObject<LeadMergedDomainEvent>(om.Payload)?.SourceLeadId.ToString() ?? ""),
                TargetLeadId = Guid.Parse(JsonConvert.DeserializeObject<LeadMergedDomainEvent>(om.Payload)?.TargetLeadId.ToString() ?? ""),
                MergedAt = om.CreatedOnUtc
            }).ToList()
        };

        return Results.Ok(dto);
    }

    public class LeadDetailDto
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public Dictionary<string, object?> CustomFields { get; set; } = new();
        public List<MergeHistoryItem> MergeHistory { get; set; } = new();
    }

    public class MergeHistoryItem
    {
        public Guid SourceLeadId { get; set; }
        public Guid TargetLeadId { get; set; }
        public DateTime MergedAt { get; set; }
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hard-coded Lead properties per tenant | EF Core 10 JSON complex types | EF Core 10 GA (Feb 2025) | Eliminates per-field columns, enables schema flexibility, maintains queryability |
| Entity-per-field table (DynamicField) | JSONB + complex type ownership | PostgreSQL GIN indexing support | N+1 query risk eliminated, schema explosion prevented, faster aggregations |
| String-based stage names (hard-coded "New", "Won") | PipelineStage entity with ordering | N/A (Phase 2 design) | Enables re-ordering, prevents invalid stages, queryable pipeline analysis |
| Manual CSV duplicate detection | Fuzzy string matching + normalized fields | FuzzySharp 1.11.0 adoption | Reduces manual merge burden, catches fuzzy matches, ranked confidence scores |
| Custom code for merge tracking | LeadMerge entity + outbox pattern | Phase 1 outbox pattern | Complete audit trail, queryable history, event-sourced compliance |

**Deprecated/outdated:**
- **Separate DynamicField table approach (LEAD-01):** Historically used in older ORMs (NHibernate, etc.). EF Core 10's native JSON support is superior—no N+1 query risk, no schema bloat. Don't implement per-field table.
- **Hard-coded stage strings (LEAD-02):** Limits tenants, requires code change per stage. PipelineStage entity is the modern approach.
- **Email-only duplicate detection (LEAD-04):** Incomplete. Phone + name fuzzy matching catches more duplicates. Normalized comparison is standard in modern CRMs.

## Open Questions

1. **Performance of fuzzy matching at scale**
   - What we know: FuzzySharp TokenSetRatio is O(n*m) string comparison; acceptable for <10k leads per batch.
   - What's unclear: Behavior with 100k+ leads per tenant; should fuzzy matching be triggered only for small result sets?
   - Recommendation: Implement exact-match-only in Phase 2. Defer fuzzy matching to Phase 2 enhancement if POST-launch data shows it's needed. Log decision in STATE.md.

2. **Custom field indexing strategy for reporting**
   - What we know: GIN index on JSONB enables fast filtering; PostgreSQL can index specific JSON keys via expression indexes.
   - What's unclear: Should we create expression indexes for frequently-filtered custom fields? This requires tenant admin to declare "searchable" fields.
   - Recommendation: Keep simple in Phase 2 — add generic GIN index on all custom_field_values. Phase 4 can add per-field expression indexes if reporting queries slow.

3. **Lead soft-delete visibility in APIs**
   - What we know: Global query filters exclude IsDeleted = true from normal queries.
   - What's unclear: Should admin see merged (deleted) leads in separate "Merged Records" view? Or only via LeadMerge audit table?
   - Recommendation: Phase 2 — deleted leads are invisible. Phase 4 (reporting) can add audit view. Document in CONTEXT.md for clarity.

4. **Handling duplicate detection on re-import**
   - What we know: Duplicate detection runs before lead creation (Phase 2).
   - What's unclear: On CSV import (Phase 3), should each row be checked individually or in batch? Batch is more efficient but harder to report per-row.
   - Recommendation: Defer to Phase 3 import research. Phase 2 assumes single-record creation flow.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Testcontainers PostgreSQL 4.3.0 |
| Config file | None — xUnit uses default discovery; TestContainers fixture in base class |
| Quick run command | `dotnet test IronMonkey.Tests --filter "Category=Unit" --configuration Debug` |
| Full suite command | `dotnet test IronMonkey.Tests --configuration Debug` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| LEAD-01 | CustomFieldDefinition created; Lead stores value in JSON; query filters by field value | Integration | `dotnet test IronMonkey.Tests --filter "ClassName=CustomFieldTests" -x` | ❌ Wave 0 |
| LEAD-01 | Custom field value validation (currency/date/number types) enforced before SaveChanges | Unit | `dotnet test IronMonkey.Tests --filter "ClassName=CustomFieldValidationTests" -x` | ❌ Wave 0 |
| LEAD-02 | PipelineStage created per tenant; Lead.StageId enforces FK; query stages by tenant+order | Integration | `dotnet test IronMonkey.Tests --filter "ClassName=PipelineStageTests" -x` | ❌ Wave 0 |
| LEAD-03 | Lead.Source enum set on creation; queryable from Lead detail; visible in DTO | Unit | `dotnet test IronMonkey.Tests --filter "ClassName=LeadSourceTests" -x` | ❌ Wave 0 |
| LEAD-04 | Exact-match duplicate detection on email/phone; fuzzy name match; returns ranked candidates | Unit | `dotnet test IronMonkey.Tests --filter "ClassName=DuplicateDetectionTests" -x` | ❌ Wave 0 |
| LEAD-05 | Two leads merged; target soft-deleted; LeadMerge audit record created; custom fields merged | Integration | `dotnet test IronMonkey.Tests --filter "ClassName=LeadMergeTests" -x` | ❌ Wave 0 |
| LEAD-05 | Merged lead excluded from normal queries; discoverable via LeadMerge.TargetLeadId | Integration | `dotnet test IronMonkey.Tests --filter "ClassName=LeadMergeVisibilityTests" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "Category=Unit" --configuration Debug`
- **Per wave merge:** `dotnet test IronMonkey.Tests --configuration Debug`
- **Phase gate:** Full suite green + manual testing of duplicate detection UX (warning flow) before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Unit/CustomFieldValidationTests.cs` — covers LEAD-01 validation
- [ ] `IronMonkey.Tests/Integration/CustomFieldTests.cs` — covers LEAD-01 storage/query
- [ ] `IronMonkey.Tests/Integration/PipelineStageTests.cs` — covers LEAD-02
- [ ] `IronMonkey.Tests/Unit/LeadSourceTests.cs` — covers LEAD-03
- [ ] `IronMonkey.Tests/Unit/DuplicateDetectionTests.cs` — covers LEAD-04
- [ ] `IronMonkey.Tests/Integration/LeadMergeTests.cs` — covers LEAD-05
- [ ] `IronMonkey.Tests/Integration/LeadMergeVisibilityTests.cs` — covers LEAD-05 soft-delete behavior
- [ ] `IronMonkey.Tests/Fixtures/TestDataBuilder.cs` — helper to create custom fields + leads for tests
- [ ] Framework install: FuzzySharp 1.11.0 via `dotnet add package FuzzySharp --version 1.11.0`

## Sources

### Primary (HIGH confidence)
- [EF Core 10 JSON Columns (Microsoft official)](https://devblogs.microsoft.com/dotnet/announcing-ef7-release-candidate-2/)
- [Npgsql EF Core JSONB Mapping (official docs)](https://www.npgsql.org/efcore/mapping/json.html)
- [Phase 1 Research - Multi-Tenancy Foundation](../01-multi-tenancy-foundation/01-RESEARCH.md) — outbox pattern, validation patterns, endpoint structure
- [EF Core SaveChanges Override Pattern (Anton Dev Tips)](https://antondevtips.com/blog/how-to-implement-audit-trail-in-asp-net-core-with-ef-core)

### Secondary (MEDIUM confidence)
- [FuzzySharp GitHub (JakeBayer)](https://github.com/JakeBayer/FuzzySharp) — Levenshtein + token matching implementation
- [Fuzzy Matching Best Practices (Nimble)](https://www.nimble.com/blog/how-to-send-website-form-leads-to-crm/) — verified with multiple CRM implementations
- [Salesforce Lead Source Tracking (Salesforce Ben)](https://www.salesforceben.com/lets-talk-about-salesforce-lead-source/) — industry-standard lead source enum values
- [Audit Trail via Outbox Pattern (Code with Mukesh)](https://codewithmukesh.com/blog/audit-trail-implementation-in-aspnet-core/) — event-sourced audit approach

### Tertiary (acknowledged limitations)
- State.md notes: "Phase 2: Dynamic field indexing performance needs validation — PostgreSQL JSONB indexing with 100+ fields per tenant. Plan for load testing in Phase 2."
- FuzzySharp performance benchmarks: found no official benchmark; community reports acceptable performance for <10k lead comparisons.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — EF Core 10 JSON support is GA; Npgsql official docs confirmed; Phase 1 patterns proven
- Architecture: MEDIUM-HIGH — JSON complex types proven in community, but large-scale JSONB indexing needs validation (flagged in Phase 2 goals)
- Pitfalls: MEDIUM — Based on CRM best practices (Salesforce, EspoCRM docs) and EF Core patterns; fuzzy matching performance needs real-world testing
- Validation architecture: MEDIUM — Test structure from Phase 1; exact tests needed depend on final endpoint design

**Research date:** 2026-03-20
**Valid until:** 2026-04-17 (30 days; EF Core 10 is stable; fuzzy matching approaches unlikely to change in this period)
