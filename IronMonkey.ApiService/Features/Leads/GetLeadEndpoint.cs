using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads;

public class GetLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads/{id:guid}", Handle)
        .WithSummary("Get a single lead by id")
        .WithTags("Leads")
        .RequireAuthorization();

    public record Response(
        Guid Id, string FirstName, string LastName, string Email, string Mobile,
        string Source, Guid PipelineStageId, string StageName, bool IsConverted,
        Guid? ConvertedContactId, Guid? ConvertedOpportunityId,
        Guid? AssignedToUserId, DateTime CreatedAt,
        Dictionary<string, object?> CustomFields);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads
            .Include(l => l.Stage)
            .SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (lead is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new Response(
            lead.Id, lead.FirstName, lead.LastName, lead.Email, lead.Mobile,
            lead.Source.ToString(), lead.PipelineStageId, lead.Stage?.Name ?? "—",
            lead.IsConverted, lead.ConvertedContactId, lead.ConvertedOpportunityId,
            lead.AssignedToUserId, lead.CreatedAt, lead.CustomFields.Values));
    }
}
