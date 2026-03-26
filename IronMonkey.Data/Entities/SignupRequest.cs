using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class SignupRequest : Entity
{
    private SignupRequest(
        Guid id,
        string companyName,
        string adminEmail,
        string adminPasswordHash,
        string phone,
        Guid? recipeId,
        string companySize,
        string address,
        string billingContact)
        : base(id)
    {
        CompanyName = companyName;
        AdminEmail = adminEmail;
        AdminPasswordHash = adminPasswordHash;
        Phone = phone;
        RecipeId = recipeId;
        CompanySize = companySize;
        Address = address;
        BillingContact = billingContact;
        Status = "Pending";
    }

    private SignupRequest() { }

    public string CompanyName { get; private set; } = string.Empty;
    public string AdminEmail { get; private set; } = string.Empty;
    public string AdminPasswordHash { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public Guid? RecipeId { get; private set; }
    public string CompanySize { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string BillingContact { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;  // Pending, Approved, Rejected
    public string? ReviewNote { get; private set; }
    public Guid? TenantId { get; private set; }  // Set after tenant record created

    public static SignupRequest Create(
        string companyName, string adminEmail, string adminPasswordHash,
        string phone, Guid? recipeId, string companySize,
        string address, string billingContact)
    {
        return new SignupRequest(
            Guid.NewGuid(), companyName, adminEmail, adminPasswordHash,
            phone, recipeId, companySize, address, billingContact);
    }

    public void Approve(string? note = null)
    {
        Status = "Approved";
        ReviewNote = note;
    }

    public void Reject(string? note = null)
    {
        Status = "Rejected";
        ReviewNote = note;
    }

    public void LinkTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
