using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public class CreateWebFormEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/web-forms", Handle)
        .WithSummary("Create an embeddable web form for lead capture")
        .WithTags("Web Forms")
        .RequireAuthorization();

    public record Request(
        string FormName,
        List<string> FieldNames,
        Guid DefaultPipelineStageId,
        string? PostSubmissionRedirectUrl = null);

    public record Response(Guid FormId, string FormToken, string HostedUrl);

    private static async Task<Results<Ok<Response>, BadRequest<string>>> Handle(
        Request request,
        IWebFormService webFormService,
        ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FormName))
            return TypedResults.BadRequest("FormName is required");

        if (request.FieldNames == null || request.FieldNames.Count == 0)
            return TypedResults.BadRequest("At least one field must be selected");

        var tenantId = tenantService.GetCurrentTenantId();
        var created = await webFormService.CreateAsync(
            tenantId, request.FormName, request.FieldNames,
            request.DefaultPipelineStageId, request.PostSubmissionRedirectUrl, cancellationToken);

        return TypedResults.Ok(new Response(created.FormId, created.FormToken, created.HostedUrl));
    }
}
