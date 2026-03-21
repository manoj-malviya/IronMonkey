using System.Text.Json;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Tenant-configured web form that creates leads on submission.
/// FormToken is the unique identifier embedded in the hosted URL: /forms/{FormToken}
/// FieldNames is JSON array of field names to display on the form.
/// </summary>
public sealed class WebForm : Entity
{
    private WebForm(Guid id, Guid tenantId, string formName, string formToken,
        List<string> fieldNames, Guid defaultPipelineStageId)
        : base(id)
    {
        TenantId = tenantId;
        FormName = formName;
        FormToken = formToken;
        FieldNamesJson = JsonSerializer.Serialize(fieldNames);
        DefaultPipelineStageId = defaultPipelineStageId;
        IsActive = true;
    }

    private WebForm() { }

    public Guid TenantId { get; private set; }
    public string FormName { get; private set; } = string.Empty;
    public string FormToken { get; private set; } = string.Empty;

    /// <summary>JSON array of field names to show: ["FirstName","LastName","Email","Mobile"]</summary>
    public string FieldNamesJson { get; private set; } = "[]";

    public Guid DefaultPipelineStageId { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Optional redirect URL after submission. Null = show thank-you message.</summary>
    public string? PostSubmissionRedirectUrl { get; private set; }

    public List<string> GetFieldNames()
        => JsonSerializer.Deserialize<List<string>>(FieldNamesJson) ?? new List<string>();

    public static WebForm Create(Guid tenantId, string formName, string formToken,
        List<string> fieldNames, Guid defaultPipelineStageId, string? redirectUrl = null)
    {
        var form = new WebForm(Guid.NewGuid(), tenantId, formName, formToken, fieldNames, defaultPipelineStageId);
        form.PostSubmissionRedirectUrl = redirectUrl;
        return form;
    }

    public void Deactivate() => IsActive = false;
}
