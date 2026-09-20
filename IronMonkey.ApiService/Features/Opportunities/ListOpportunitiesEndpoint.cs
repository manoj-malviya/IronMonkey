using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Opportunities;

public class ListOpportunitiesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities", Handle)
        .WithSummary("List opportunities for the current tenant with optional search and stage filter")
        .WithTags("Opportunities")
        .RequireAuthorization();

    public record OpportunityItem(
        Guid Id, string Title, Guid ContactId, string ContactName,
        string Stage, decimal Amount, DateTime ExpectedCloseDate,
        string? LossReason, DateTime CreatedAt);

    private static async Task<Ok<List<OpportunityItem>>> Handle(
        string? search,
        string? stage,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Opportunities.Include(o => o.Contact).AsQueryable();

        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(o => o.Stage == stage);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(o =>
                EF.Functions.ILike(o.Title, pattern) ||
                EF.Functions.ILike(o.Contact.Name, pattern));
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var result = opportunities.Select(o => new OpportunityItem(
            o.Id, o.Title, o.ContactId, o.Contact?.Name ?? "—",
            o.Stage, o.Amount, o.ExpectedCloseDate, o.LossReason, o.CreatedAt)).ToList();

        return TypedResults.Ok(result);
    }
}
