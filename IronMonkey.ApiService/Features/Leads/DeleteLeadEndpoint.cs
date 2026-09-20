using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads;

public class DeleteLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/leads/{id:guid}", Handle)
        .WithSummary("Soft-delete a lead")
        .WithTags("Leads")
        .RequireAuthorization();

    public record Response(string Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
            return TypedResults.NotFound();

        // Soft delete: the global query filter excludes IsDeleted, so this hides the lead
        // from every read path without breaking rows that reference it (merges, activity).
        lead.IsDeleted = true;
        lead.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Lead deleted successfully."));
    }
}
