using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public record CreatedWebForm(Guid FormId, string FormToken, string HostedUrl);

public record WebFormSummary(Guid FormId, string FormName, string FormToken, string HostedUrl, DateTime CreatedAt);

public interface IWebFormService
{
    /// <summary>
    /// Creates a new web form with a unique token. Returns the hosted URL.
    /// </summary>
    Task<CreatedWebForm> CreateAsync(
        Guid tenantId,
        string formName,
        List<string> fieldNames,
        Guid defaultPipelineStageId,
        string? postSubmissionRedirectUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Looks up a form by its token. Returns null if not found or inactive.
    /// Used by both the GET (render page) and POST (submit) endpoints.
    /// </summary>
    Task<Data.Entities.WebForm?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Gets the tenant connection string for creating leads in the correct tenant DB.
    /// </summary>
    Task<string> GetTenantConnectionStringAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Deactivates (soft-removes) a web form. Token no longer accepts submissions.
    /// </summary>
    Task DeleteAsync(Guid tenantId, Guid formId, CancellationToken ct = default);

    /// <summary>
    /// Lists all active web forms for the tenant.
    /// </summary>
    Task<List<WebFormSummary>> ListAsync(Guid tenantId, CancellationToken ct = default);
}
