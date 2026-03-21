using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads;

public class CreateLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads", Handle)
        .WithSummary("Create a new lead with source tracking and pipeline stage assignment")
        .WithTags("Leads")
        .RequireAuthorization();

    public record Request(
        string FirstName,
        string LastName,
        string Mobile,
        string Email,
        string Source,
        Guid PipelineStageId);

    public record Response(
        Guid Id,
        string FirstName,
        string LastName,
        string Mobile,
        string Email,
        string Source,
        Guid PipelineStageId,
        Dictionary<string, object?> CustomFields);

    private static async Task<Results<Created<Response>, BadRequest<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LeadSource>(request.Source, ignoreCase: true, out var source))
            return TypedResults.BadRequest("Invalid source value. Valid: Manual, Import, Api, WebForm");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stageExists = await db.PipelineStages
            .AnyAsync(p => p.Id == request.PipelineStageId, cancellationToken);

        if (!stageExists)
            return TypedResults.BadRequest("Invalid pipeline stage");

        var lead = Lead.Create(tenantId, request.FirstName, request.LastName, request.Mobile, request.Email, source, request.PipelineStageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(
            lead.Id,
            lead.FirstName,
            lead.LastName,
            lead.Mobile,
            lead.Email,
            lead.Source.ToString(),
            lead.PipelineStageId,
            lead.CustomFields.Values);

        return TypedResults.Created($"/api/leads/{lead.Id}", response);
    }
}
