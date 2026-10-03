using Microsoft.EntityFrameworkCore;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Insights;

/// <summary>How a field's values compare, aggregate and group.</summary>
public enum FieldKind
{
    Text,
    Number,

    /// <summary>A timestamptz column: compared as instants, bucketed in the tenant's zone.</summary>
    Instant,

    /// <summary>A calendar date (custom Date fields): compared as tenant-local dates.</summary>
    Date,
    Bool,
    UserRef,
    MultiSelect
}

/// <summary>
/// One field a filter, column, sort, grouping or measure may name.
///
/// <para><b>The SQL here is the allow-list.</b> Every expression is a constant written in this
/// file, or — for a custom field — a fixed jsonb path whose key is bound as a parameter. Nothing
/// a tenant sends is ever spliced into SQL text: an unknown field key is rejected before any SQL
/// exists, so a hostile key like <c>"Id\"; DROP TABLE leads; --"</c> can only produce a validation
/// error.</para>
/// </summary>
public sealed record FieldDef(string Key, string Label, FieldKind Kind, string Sql, bool Sortable = true,
    bool Groupable = false, string? ParameterValue = null, List<string>? Options = null)
{
    /// <summary>For custom fields: the jsonb key (the definition id), bound as a parameter.</summary>
    public bool IsCustom => ParameterValue is not null;
}

/// <summary>A record type's table, joins, ownership and fields.</summary>
public sealed record RecordDef(
    InsightRecordType Type,
    string From,
    string ReadPermission,
    Dictionary<string, FieldDef> Fields,
    CustomFieldEntity? CustomScope);

public static class RecordCatalog
{
    public const string CustomPrefix = "cf:";

    /// <summary>
    /// Built-in fields. The alias <c>t</c> is the record table; <c>s</c> its stage; <c>l</c>, for
    /// tasks, the lead the task belongs to.
    /// </summary>
    private static readonly Dictionary<InsightRecordType, (string From, string Permission, CustomFieldEntity? Scope, FieldDef[] Fields)> Builtins = new()
    {
        [InsightRecordType.Lead] = (
            "leads t LEFT JOIN pipeline_stages s ON s.\"Id\" = t.\"PipelineStageId\"",
            PermissionConstants.LeadsRead, CustomFieldEntity.Lead,
            [
                new("firstName", "First name", FieldKind.Text, "t.\"FirstName\""),
                new("lastName", "Last name", FieldKind.Text, "t.\"LastName\""),
                new("email", "Email", FieldKind.Text, "t.\"Email\""),
                new("mobile", "Mobile", FieldKind.Text, "t.\"Mobile\""),
                new("source", "Source", FieldKind.Text, "t.\"Source\"", Groupable: true, Options: ["Manual", "Import", "Api", "WebForm"]),
                new("stage", "Stage", FieldKind.Text, "s.\"Name\"", Groupable: true),
                new("assignedTo", "Assigned to", FieldKind.UserRef, "t.\"AssignedToUserId\"", Groupable: true),
                new("isConverted", "Converted", FieldKind.Bool, "t.\"IsConverted\"", Groupable: true),
                new("createdAt", "Created", FieldKind.Instant, "t.\"CreatedAt\""),
                new("updatedAt", "Updated", FieldKind.Instant, "t.\"UpdatedAt\"")
            ]),
        [InsightRecordType.Contact] = (
            "contacts t",
            PermissionConstants.ContactsRead, CustomFieldEntity.Contact,
            [
                new("name", "Name", FieldKind.Text, "t.\"Name\""),
                new("email", "Email", FieldKind.Text, "t.\"Email\""),
                new("mobile", "Mobile", FieldKind.Text, "t.\"Mobile\""),
                new("owner", "Owner", FieldKind.UserRef, "t.\"OwnerUserId\"", Groupable: true),
                new("createdAt", "Created", FieldKind.Instant, "t.\"CreatedAt\"")
            ]),
        [InsightRecordType.Opportunity] = (
            "opportunities t LEFT JOIN pipeline_stages s ON s.\"Id\" = t.\"PipelineStageId\"",
            PermissionConstants.OpportunitiesRead, null,
            [
                new("title", "Title", FieldKind.Text, "t.\"Title\""),
                new("amount", "Amount", FieldKind.Number, "t.\"Amount\""),
                new("recurringAmount", "Recurring amount", FieldKind.Number, "t.\"RecurringAmount\""),
                new("currency", "Currency", FieldKind.Text, "t.\"CurrencyCode\"", Groupable: true),
                new("stage", "Stage", FieldKind.Text, "s.\"Name\"", Groupable: true),
                new("stageType", "Stage type", FieldKind.Text, "s.\"StageType\"", Groupable: true,
                    Options: ["Entry", "Active", "ClosedWon", "ClosedLost"]),
                new("owner", "Owner", FieldKind.UserRef, "t.\"OwnerUserId\"", Groupable: true),
                new("expectedCloseDate", "Expected close", FieldKind.Instant, "t.\"ExpectedCloseDate\""),
                new("createdAt", "Created", FieldKind.Instant, "t.\"CreatedAt\"")
            ]),
        [InsightRecordType.Task] = (
            "lead_tasks t JOIN leads l ON l.\"Id\" = t.\"LeadId\"",
            PermissionConstants.LeadsRead, null,
            [
                new("title", "Title", FieldKind.Text, "t.\"Title\""),
                new("status", "Status", FieldKind.Text, "t.\"Status\"", Groupable: true,
                    Options: ["Pending", "InProgress", "Completed", "Cancelled"]),
                new("priority", "Priority", FieldKind.Text, "t.\"Priority\"", Groupable: true,
                    Options: ["Low", "Medium", "High", "Urgent"]),
                new("assignedTo", "Assigned to", FieldKind.UserRef, "t.\"AssignedToUserId\"", Groupable: true),
                new("dueDate", "Due", FieldKind.Instant, "t.\"DueDate\""),
                new("createdAt", "Created", FieldKind.Instant, "t.\"CreatedAt\"")
            ])
    };

    /// <summary>Loads a record type's definition, including the tenant's custom fields for it.</summary>
    public static async Task<RecordDef> LoadAsync(TenantDbContext db, InsightRecordType type, CancellationToken ct)
    {
        var (from, permission, scope, builtins) = Builtins[type];
        var fields = builtins.ToDictionary(f => f.Key, StringComparer.Ordinal);

        if (scope is { } customScope)
        {
            var definitions = await db.CustomFieldDefinitions.AsNoTracking()
                .Where(f => f.AppliesTo == customScope)
                .ToListAsync(ct);

            foreach (var d in definitions)
            {
                var key = CustomPrefix + d.Id.ToString("D");
                var kind = d.FieldType switch
                {
                    CustomFieldType.Number or CustomFieldType.Currency => FieldKind.Number,
                    CustomFieldType.Date => FieldKind.Date,
                    CustomFieldType.Boolean => FieldKind.Bool,
                    CustomFieldType.MultiSelect => FieldKind.MultiSelect,
                    _ => FieldKind.Text
                };
                // The jsonb key placeholder {key} is replaced by a bound parameter name at
                // compile time; the definition id never appears in SQL text.
                fields[key] = new FieldDef(key, d.FieldName + (d.IsArchived ? " (archived)" : ""), kind,
                    "{key}", Sortable: kind != FieldKind.MultiSelect,
                    Groupable: kind is FieldKind.Text or FieldKind.Bool, ParameterValue: d.Id.ToString(),
                    Options: d.Options.Count > 0 ? d.Options : null);
            }
        }

        return new RecordDef(type, from, permission, fields, scope);
    }
}
