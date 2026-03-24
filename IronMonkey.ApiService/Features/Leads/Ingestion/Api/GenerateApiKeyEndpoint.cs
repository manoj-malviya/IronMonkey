using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Api;

public class GenerateApiKeyEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/api-keys", Handle)
        .WithSummary("Generate a new API key for external lead ingestion")
        .WithTags("API Keys")
        .RequireAuthorization();

    public record Response(Guid KeyId, string PlaintextKey, string KeyPrefix,
        string Warning = "Store this key securely — it will not be shown again.");

    private static async Task<Ok<Response>> Handle(
        IApiKeyService apiKeyService,
        ITenantService tenantService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var generated = await apiKeyService.GenerateAsync(tenantId, cancellationToken);
        return TypedResults.Ok(new Response(generated.KeyId, generated.PlaintextKey, generated.KeyPrefix));
    }
}
