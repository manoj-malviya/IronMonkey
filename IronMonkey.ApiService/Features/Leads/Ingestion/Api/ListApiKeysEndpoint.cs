using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public class ListApiKeysEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/api-keys", Handle)
        .WithSummary("List active API keys (prefix only)")
        .WithTags("API Keys")
        .RequireAuthorization();

    public record Response(List<ApiKeyInfo> Keys);

    private static async Task<Ok<Response>> Handle(
        IApiKeyService apiKeyService,
        ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var keys = await apiKeyService.ListAsync(tenantId, cancellationToken);
        return TypedResults.Ok(new Response(keys));
    }
}
