using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum CustomFieldType { Text, Number, Date, Dropdown, MultiSelect, Currency, Boolean }

public sealed class CustomFieldDefinition : BaseTenantEntity
{
    private CustomFieldDefinition() { }

    public string FieldName { get; private set; } = string.Empty;
    public CustomFieldType FieldType { get; private set; }
    public bool IsRequired { get; private set; }
    public List<string> Options { get; private set; } = []; // for Dropdown/MultiSelect

    public static CustomFieldDefinition Create(Guid tenantId, string fieldName, CustomFieldType type, bool isRequired, List<string>? options = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FieldName = fieldName,
            FieldType = type,
            IsRequired = isRequired,
            Options = options ?? []
        };

    public void Update(string fieldName, CustomFieldType type, bool isRequired, List<string>? options)
    {
        FieldName = fieldName;
        FieldType = type;
        IsRequired = isRequired;
        Options = options ?? [];
    }
}
