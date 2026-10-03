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

    /// <summary>
    /// The user who owns this record, for record-level visibility. Null means unowned, which
    /// only an All scope can see. Backfilled from the converting lead's assignee by the
    /// <c>RecordVisibility</c> migration; set to the creator on create.
    /// </summary>
    public Guid? OwnerUserId { get; private set; }

    public void AssignOwner(Guid? ownerUserId) => OwnerUserId = ownerUserId;
    public string Mobile { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public CustomFieldValues CustomFields { get; private set; } = new();

    /// <summary>
    /// The mobile number reduced to digits, maintained by PostgreSQL as a stored generated
    /// column and trigram-indexed, so search can match "07700 900123" against "+447700900123"
    /// without normalising every row at query time. Read-only to the application.
    /// </summary>
    public string MobileDigits { get; private set; } = string.Empty;

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
