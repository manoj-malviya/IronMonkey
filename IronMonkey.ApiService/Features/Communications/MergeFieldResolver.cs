using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications;

/// <summary>
/// Builds the placeholder dictionary a template renders against.
///
/// Custom fields are exposed under their <em>label</em> for the template author, but resolved
/// through the definition <em>id</em>, which is how values are actually keyed. That indirection
/// matters: renaming a custom field must not break every template that uses it, and two fields
/// must not collide because their labels happen to match.
///
/// Built-in names are fixed and cannot be shadowed by a custom field, so a field called
/// "FirstName" cannot quietly replace the lead's real first name in an outgoing message.
/// </summary>
public interface IMergeFieldResolver
{
    Task<IReadOnlyDictionary<string, string?>> ForLeadAsync(TenantDbContext db, Lead lead, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string?>> ForContactAsync(TenantDbContext db, Contact contact, CancellationToken cancellationToken);

    /// <summary>
    /// Every placeholder name valid for this tenant, for validating a template at save time
    /// and for offering the author a list.
    /// </summary>
    Task<IReadOnlyList<string>> AvailableFieldsAsync(TenantDbContext db, CancellationToken cancellationToken);
}

public sealed class MergeFieldResolver : IMergeFieldResolver
{
    /// <summary>
    /// Built-in lead placeholders. Kept as a set so custom fields can be checked against it
    /// and refused the chance to shadow one.
    /// </summary>
    private static readonly string[] LeadBuiltIns =
        ["FirstName", "LastName", "FullName", "Email", "Mobile", "Source", "StageName"];

    private static readonly string[] ContactBuiltIns = ["Name", "Email", "Mobile"];

    public async Task<IReadOnlyDictionary<string, string?>> ForLeadAsync(
        TenantDbContext db, Lead lead, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = lead.FirstName,
            ["LastName"] = lead.LastName,
            ["FullName"] = $"{lead.FirstName} {lead.LastName}".Trim(),
            ["Email"] = lead.Email,
            ["Mobile"] = lead.Mobile,
            ["Source"] = lead.Source.ToString(),
            ["StageName"] = lead.Stage?.Name ?? string.Empty
        };

        await AddCustomFieldsAsync(db, values, lead.CustomFields, CustomFieldEntity.Lead, cancellationToken);
        return values;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ForContactAsync(
        TenantDbContext db, Contact contact, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = contact.Name,
            ["Email"] = contact.Email,
            ["Mobile"] = contact.Mobile
        };

        await AddCustomFieldsAsync(db, values, contact.CustomFields, CustomFieldEntity.Contact, cancellationToken);
        return values;
    }

    public async Task<IReadOnlyList<string>> AvailableFieldsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var names = new List<string>(LeadBuiltIns);

        foreach (var builtIn in ContactBuiltIns)
        {
            if (!names.Contains(builtIn, StringComparer.OrdinalIgnoreCase))
                names.Add(builtIn);
        }

        // Both scopes are offered here on purpose: a template is not bound to a record type,
        // so validation accepts any real field. Whether a given field resolves depends on what
        // the template is rendered against, which is reported at render time.
        var labels = await db.CustomFieldDefinitions
            .AsNoTracking()
            .Where(definition => !definition.IsArchived)
            .Select(definition => definition.FieldName)
            .ToListAsync(cancellationToken);

        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label) && !names.Contains(label, StringComparer.OrdinalIgnoreCase))
                names.Add(label);
        }

        return names;
    }

    private static async Task AddCustomFieldsAsync(
        TenantDbContext db,
        Dictionary<string, string?> values,
        CustomFieldValues? customFields,
        CustomFieldEntity scope,
        CancellationToken cancellationToken)
    {
        // Scoped to the record type being rendered. Without the filter, a lead template would
        // offer contact-only fields — which then always render empty, because the lead's
        // values are keyed by different definition ids entirely.
        //
        // Archived fields are deliberately still resolved here, unlike in AvailableFieldsAsync
        // which offers only active ones. Archiving a field must not break a template already
        // using it: the stored value is still there, and the alternative is a template that
        // starts failing to render the moment someone tidies up a field list.
        var definitions = await db.CustomFieldDefinitions
            .AsNoTracking()
            .Where(definition => definition.AppliesTo == scope)
            .Select(definition => new { definition.Id, definition.FieldName })
            .ToListAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.FieldName)) continue;

            // A custom field never overwrites a built-in. Otherwise a tenant could create a
            // field named "Email" and silently redirect what every template renders.
            if (values.ContainsKey(definition.FieldName)) continue;

            // Values are keyed by definition id, never by name — the same rule the custom
            // field binder follows, so a rename does not orphan the stored value.
            var raw = customFields?.Get(definition.Id.ToString());
            values[definition.FieldName] = raw?.ToString() ?? string.Empty;
        }
    }
}
