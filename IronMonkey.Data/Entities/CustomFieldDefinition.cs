using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum CustomFieldType { Text, Number, Date, Dropdown, MultiSelect, Currency, Boolean }

/// <summary>Which record a custom field is captured on. A field belongs to exactly one.</summary>
public enum CustomFieldEntity { Lead = 0, Contact = 1 }

public sealed class CustomFieldDefinition : BaseTenantEntity
{
    private CustomFieldDefinition() { }

    /// <summary>The label shown on a form. Free to change without touching stored values.</summary>
    public string FieldName { get; private set; } = string.Empty;

    /// <summary>
    /// Stable internal key for integrations and routing, unique per scope. Values are keyed
    /// by the definition Id, not this — the key exists so an external caller has something
    /// to address that does not move when the label is edited.
    /// </summary>
    public string FieldKey { get; private set; } = string.Empty;

    public CustomFieldType FieldType { get; private set; }
    public bool IsRequired { get; private set; }
    public List<string> Options { get; private set; } = []; // for Dropdown/MultiSelect

    /// <summary>Guidance rendered under the input on a form. Optional.</summary>
    public string? HelpText { get; private set; }

    /// <summary>
    /// Value pre-filled on a create form, stored as text and converted by the binder like
    /// any submitted value. Null means no default.
    /// </summary>
    public string? DefaultValue { get; private set; }

    /// <summary>
    /// Archived fields keep their captured values but no longer render on forms. Archiving
    /// is how a field in use is retired — deleting one would orphan every stored value.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>The record this field is captured on. Defaults to Lead so fields defined
    /// before this existed keep their original meaning.</summary>
    public CustomFieldEntity AppliesTo { get; private set; } = CustomFieldEntity.Lead;

    /// <summary>Order the field appears in on a form, ascending.</summary>
    public int DisplayOrder { get; private set; }

    public static CustomFieldDefinition Create(
        Guid tenantId,
        string fieldName,
        CustomFieldType type,
        bool isRequired,
        List<string>? options = null,
        CustomFieldEntity appliesTo = CustomFieldEntity.Lead,
        int displayOrder = 0,
        string? fieldKey = null,
        string? helpText = null,
        string? defaultValue = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FieldName = fieldName,
            FieldKey = string.IsNullOrWhiteSpace(fieldKey) ? DeriveKey(fieldName) : fieldKey.Trim(),
            FieldType = type,
            IsRequired = isRequired,
            Options = options ?? [],
            AppliesTo = appliesTo,
            DisplayOrder = displayOrder,
            HelpText = helpText,
            DefaultValue = defaultValue
        };

    public void Update(
        string fieldName,
        CustomFieldType type,
        bool isRequired,
        List<string>? options,
        CustomFieldEntity? appliesTo = null,
        int? displayOrder = null,
        string? fieldKey = null,
        string? helpText = null,
        string? defaultValue = null)
    {
        FieldName = fieldName;
        FieldType = type;
        IsRequired = isRequired;
        Options = options ?? [];
        if (appliesTo.HasValue) AppliesTo = appliesTo.Value;
        if (displayOrder.HasValue) DisplayOrder = displayOrder.Value;
        if (!string.IsNullOrWhiteSpace(fieldKey)) FieldKey = fieldKey.Trim();
        HelpText = helpText;
        DefaultValue = defaultValue;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;

    /// <summary>
    /// Builds a key from a label: lowercase, non-alphanumerics collapsed to underscores.
    /// Used for the backfill and whenever the Admin leaves the key blank.
    /// </summary>
    public static string DeriveKey(string fieldName)
    {
        var chars = fieldName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        var key = new string(chars);
        while (key.Contains("__")) key = key.Replace("__", "_");
        key = key.Trim('_');

        return key.Length == 0 ? "field" : key;
    }
}
