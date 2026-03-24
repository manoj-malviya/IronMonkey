using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public class WebFormService : IWebFormService
{
    private readonly CentralDbContext _centralDb;
    private readonly IConfiguration _configuration;
    private readonly ITenantRegistry _tenantRegistry;

    public WebFormService(CentralDbContext centralDb, IConfiguration configuration, ITenantRegistry tenantRegistry)
    {
        _centralDb = centralDb;
        _configuration = configuration;
        _tenantRegistry = tenantRegistry;
    }

    public async Task<CreatedWebForm> CreateAsync(
        Guid tenantId,
        string formName,
        List<string> fieldNames,
        Guid defaultPipelineStageId,
        string? postSubmissionRedirectUrl,
        CancellationToken ct = default)
    {
        // Generate URL-safe token: 24 random bytes → base64 → 32 chars
        var tokenBytes = new byte[24];
        System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
        var formToken = Convert.ToBase64String(tokenBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var form = Data.Entities.WebForm.Create(
            tenantId, formName, formToken, fieldNames,
            defaultPipelineStageId, postSubmissionRedirectUrl);

        _centralDb.WebForms.Add(form);
        await _centralDb.SaveChangesAsync(ct);

        var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:5000";
        var hostedUrl = $"{baseUrl}/forms/{formToken}";

        return new CreatedWebForm(form.Id, formToken, hostedUrl);
    }

    public async Task<Data.Entities.WebForm?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return await _centralDb.WebForms
            .FirstOrDefaultAsync(f => f.FormToken == token && f.IsActive, ct);
    }

    public async Task<string> GetTenantConnectionStringAsync(Guid tenantId, CancellationToken ct = default)
        => await _tenantRegistry.GetConnectionStringAsync(tenantId, ct);

    public async Task DeleteAsync(Guid tenantId, Guid formId, CancellationToken ct = default)
    {
        var form = await _centralDb.WebForms
            .FirstOrDefaultAsync(f => f.Id == formId && f.TenantId == tenantId, ct);

        if (form == null) return;

        form.Deactivate();
        await _centralDb.SaveChangesAsync(ct);
    }

    public async Task<List<WebFormSummary>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:5000";

        return await _centralDb.WebForms
            .Where(f => f.TenantId == tenantId && f.IsActive)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new WebFormSummary(
                f.Id, f.FormName, f.FormToken,
                baseUrl + "/forms/" + f.FormToken,
                f.CreatedAt))
            .ToListAsync(ct);
    }
}
