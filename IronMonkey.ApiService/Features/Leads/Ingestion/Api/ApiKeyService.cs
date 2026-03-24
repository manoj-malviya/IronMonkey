using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public class ApiKeyService : IApiKeyService
{
    private readonly CentralDbContext _centralDb;

    public ApiKeyService(CentralDbContext centralDb)
    {
        _centralDb = centralDb;
    }

    public async Task<GeneratedApiKey> GenerateAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Generate 32 random bytes, base64-encode for URL-safe plaintext key
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var plaintextKey = Convert.ToBase64String(keyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // First 8 chars used for identification in logs/UI — never expose full key
        var keyPrefix = plaintextKey[..8];

        // BCrypt hash with cost factor 10 — DO NOT store plaintext
        var keyHash = BC.HashPassword(plaintextKey, workFactor: 10);

        var apiKey = ApiKey.Create(tenantId, keyHash, keyPrefix);
        _centralDb.ApiKeys.Add(apiKey);
        await _centralDb.SaveChangesAsync(ct);

        return new GeneratedApiKey(apiKey.Id, plaintextKey, keyPrefix);
    }

    public async Task<Guid?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
            return null;

        // Load all active keys — BCrypt.Verify is constant-time, no timing attack
        // In high-traffic scenarios, consider keyed prefix index to narrow candidates first
        var allActiveKeys = await _centralDb.ApiKeys
            .Where(k => k.IsActive)
            .ToListAsync(ct);

        foreach (var apiKey in allActiveKeys)
        {
            if (BC.Verify(plaintextKey, apiKey.KeyHash))
                return apiKey.TenantId;
        }

        return null;
    }

    public async Task DeleteAsync(Guid tenantId, Guid keyId, CancellationToken ct = default)
    {
        var apiKey = await _centralDb.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);

        if (apiKey == null) return;

        apiKey.Deactivate();
        await _centralDb.SaveChangesAsync(ct);
    }

    public async Task<List<ApiKeyInfo>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _centralDb.ApiKeys
            .Where(k => k.TenantId == tenantId && k.IsActive)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyInfo(k.Id, k.KeyPrefix, k.CreatedAt))
            .ToListAsync(ct);
    }
}
