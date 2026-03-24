namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public record GeneratedApiKey(Guid KeyId, string PlaintextKey, string KeyPrefix);

public interface IApiKeyService
{
    /// <summary>
    /// Generates a new API key for the tenant.
    /// Returns the plaintext key ONCE — it is never stored in DB.
    /// </summary>
    Task<GeneratedApiKey> GenerateAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Validates the incoming X-Api-Key header value.
    /// Returns TenantId if key is valid and active, null otherwise.
    /// </summary>
    Task<Guid?> ValidateAsync(string plaintextKey, CancellationToken ct = default);

    /// <summary>
    /// Deactivates (soft-deletes) the API key. Key can no longer authenticate requests.
    /// </summary>
    Task DeleteAsync(Guid tenantId, Guid keyId, CancellationToken ct = default);

    /// <summary>
    /// Lists all active API keys for tenant (prefix only, never full key or hash).
    /// </summary>
    Task<List<ApiKeyInfo>> ListAsync(Guid tenantId, CancellationToken ct = default);
}

public record ApiKeyInfo(Guid KeyId, string KeyPrefix, DateTime CreatedAt);
