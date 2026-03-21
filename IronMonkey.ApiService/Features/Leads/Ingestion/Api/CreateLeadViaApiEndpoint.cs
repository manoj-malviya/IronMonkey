using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public class CreateLeadViaApiEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/external/leads", Handle)
        .WithSummary("Create a lead via external REST API using X-Api-Key authentication")
        .WithTags("External API")
        .AllowAnonymous();  // Auth is X-Api-Key header, not JWT

    public record Request(
        string FirstName,
        string LastName,
        string Mobile,
        string Email,
        Guid PipelineStageId,
        Dictionary<string, object?>? CustomFields = null);

    public record DuplicateInfo(Guid LeadId, string Name, int ConfidenceScore);

    public record Response(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        List<DuplicateInfo> Duplicates);

    private static async Task<Results<Created<Response>, UnauthorizedHttpResult, BadRequest<string>>> Handle(
        Request request,
        HttpContext httpContext,
        IApiKeyService apiKeyService,
        ITenantDbContextFactory dbContextFactory,
        IDuplicateDetectionService duplicateDetection,
        ITenantRegistry tenantRegistry,
        CancellationToken cancellationToken)
    {
        // 1. Validate X-Api-Key header
        var apiKeyHeader = httpContext.Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrWhiteSpace(apiKeyHeader))
            return TypedResults.Unauthorized();

        var tenantId = await apiKeyService.ValidateAsync(apiKeyHeader, cancellationToken);
        if (tenantId == null)
            return TypedResults.Unauthorized();

        // 2. Get tenant DB connection
        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId.Value, cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId.Value);

        // 3. Validate pipeline stage belongs to this tenant
        var stageExists = await db.PipelineStages
            .AnyAsync(p => p.Id == request.PipelineStageId, cancellationToken);
        if (!stageExists)
            return TypedResults.BadRequest("Invalid pipeline stage");

        // 4. Create lead with Source=Api
        var lead = Lead.Create(tenantId.Value, request.FirstName, request.LastName,
            request.Mobile, request.Email, LeadSource.Api, request.PipelineStageId);

        // 5. Apply custom fields if provided
        if (request.CustomFields != null)
        {
            foreach (var (key, value) in request.CustomFields)
                lead.CustomFields.Values[key] = value;
        }

        // 6. Check duplicates — create anyway, return duplicates array per D-16
        var duplicates = await duplicateDetection.FindCandidatesAsync(
            tenantId.Value, request.Email, request.Mobile,
            $"{request.FirstName} {request.LastName}", cancellationToken);

        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(
            lead.Id,
            lead.FirstName,
            lead.LastName,
            lead.Email,
            duplicates.Select(d => new DuplicateInfo(d.LeadId, d.FullName, d.ConfidenceScore)).ToList());

        return TypedResults.Created($"/api/leads/{lead.Id}", response);
    }
}
