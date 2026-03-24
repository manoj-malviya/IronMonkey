using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public class DeleteWebFormEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/web-forms/{formId:guid}", Handle)
        .WithSummary("Deactivate a web form (stops accepting submissions)")
        .WithTags("Web Forms")
        .RequireAuthorization();

    private static async Task<NoContent> Handle(
        Guid formId,
        IWebFormService webFormService,
        ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        await webFormService.DeleteAsync(tenantId, formId, cancellationToken);
        return TypedResults.NoContent();
    }
}
