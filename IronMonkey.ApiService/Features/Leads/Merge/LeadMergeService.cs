using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Merge;

public class LeadMergeService : ILeadMergeService
{
    private readonly ITenantService _tenantService;
    private readonly ITenantDbContextFactory _dbContextFactory;

    public LeadMergeService(ITenantService tenantService, ITenantDbContextFactory dbContextFactory)
    {
        _tenantService = tenantService;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Lead> MergeAsync(Guid tenantId, Guid sourceLeadId, Guid targetLeadId, Guid userId, CancellationToken ct = default)
    {
        if (sourceLeadId == targetLeadId)
            throw new ArgumentException("Cannot merge a lead with itself");

        var connectionString = await _tenantService.GetConnectionStringAsync(ct);
        await using var db = _dbContextFactory.CreateForTenant(connectionString, tenantId);

        var source = await db.Leads.FirstOrDefaultAsync(l => l.Id == sourceLeadId, ct)
            ?? throw new KeyNotFoundException($"Source lead {sourceLeadId} not found");

        var target = await db.Leads.FirstOrDefaultAsync(l => l.Id == targetLeadId, ct)
            ?? throw new KeyNotFoundException($"Target lead {targetLeadId} not found");

        // Serialize snapshots BEFORE mutation
        var sourceSnapshot = JsonSerializer.Serialize(new
        {
            source.Email,
            source.FirstName,
            source.LastName,
            Values = source.CustomFields.Values
        });

        var targetSnapshot = JsonSerializer.Serialize(new
        {
            target.Email,
            target.FirstName,
            target.LastName,
            Values = target.CustomFields.Values
        });

        // Merge custom fields: target values copied to source only if source does not already have the key or source value is null
        foreach (var (key, value) in target.CustomFields.Values)
        {
            if (!source.CustomFields.Values.ContainsKey(key) || source.CustomFields.Get(key) is null)
            {
                source.CustomFields.Set(key, value);
            }
        }

        // Soft-delete target
        target.IsDeleted = true;
        target.DeletedAt = DateTime.UtcNow;

        // Create audit record
        var mergeRecord = LeadMerge.Create(tenantId, sourceLeadId, targetLeadId, userId, sourceSnapshot, targetSnapshot);
        db.Add(mergeRecord);

        await db.SaveChangesAsync(ct);

        return source;
    }
}
