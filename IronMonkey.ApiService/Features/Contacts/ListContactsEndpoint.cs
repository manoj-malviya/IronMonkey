using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Contacts;

public class ListContactsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/contacts", Handle)
        .WithSummary("List contacts for the current tenant with optional search")
        .WithTags("Contacts")
        .RequireAuthorization();

    public record ContactItem(
        Guid Id, string Name, string Email, string Mobile,
        int OpportunityCount, DateTime CreatedAt,
        Dictionary<string, object?> CustomFields);

    private static async Task<Ok<List<ContactItem>>> Handle(
        string? search,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Contacts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern) ||
                EF.Functions.ILike(c.Email, pattern) ||
                EF.Functions.ILike(c.Mobile, pattern));
        }

        // Counted in one grouped query rather than per row, so the list stays a single
        // round-trip regardless of how many contacts come back.
        var counts = await db.Opportunities
            .GroupBy(o => o.ContactId)
            .Select(g => new { ContactId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ContactId, x => x.Count, cancellationToken);

        var contacts = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var result = contacts.Select(c => new ContactItem(
            c.Id, c.Name, c.Email, c.Mobile,
            counts.TryGetValue(c.Id, out var n) ? n : 0,
            c.CreatedAt, c.CustomFields.Values)).ToList();

        return TypedResults.Ok(result);
    }
}
