using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public class DeleteApiKeyEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/api-keys/{keyId:guid}", Handle)
        .WithSummary("Delete (deactivate) an API key")
        .WithTags("API Keys")
        .RequireAuthorization();

    private static async Task<NoContent> Handle(
        Guid keyId,
        IApiKeyService apiKeyService,
        ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        await apiKeyService.DeleteAsync(tenantId, keyId, cancellationToken);
        return TypedResults.NoContent();
    }
}
