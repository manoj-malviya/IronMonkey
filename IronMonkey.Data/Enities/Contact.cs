using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class Contact : BaseTenantEntity
{
    private Contact(Guid id, Guid tenantId, string name, string mobile, string email)
        : base(id, tenantId)
    {
        Name = name;
        Mobile = mobile;
        Email = email;
    }

    private Contact()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    public static Contact Create(Guid tenantId, string name, string mobile, string email)
    {
        return new Contact(Guid.NewGuid(), tenantId, name, mobile, email);
    }

    public void UpdateContactInfo(string name, string mobile, string email)
    {
        Name = name;
        Mobile = mobile;
        Email = email;
    }
}
