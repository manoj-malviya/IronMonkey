using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

public class ConfigureRoutingEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/routing-config", Handle)
        .WithSummary("Configure lead routing strategy for the tenant (one strategy per pipeline, per D-15)")
        .WithTags("Routing")
        .RequireAuthorization();

    /// <param name="TerritoryMapJson">
    /// Territory rules, as either the structured rule set or the original flat map. Validated
    /// before it is stored — invalid JSON is never persisted, so routing cannot be silently
    /// disabled by a bad paste into the advanced editor.
    /// </param>
    public record Request(
        string Strategy,
        string Dimension,
        string? TerritoryMapJson,
        string? CustomFieldKey);

    public record Response(Guid Id, string Strategy, string Dimension, bool IsEnabled);

    /// <summary>Field-level problems, so the editor can show them where they belong.</summary>
    public record ValidationProblem(string Message, List<string> Errors);

    private static async Task<Results<Ok<Response>, BadRequest<ValidationProblem>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RoutingStrategy>(request.Strategy, ignoreCase: true, out var strategy))
            return Invalid($"Invalid strategy. Valid: {string.Join(", ", Enum.GetNames<RoutingStrategy>())}");

        if (!Enum.TryParse<RoutingDimension>(request.Dimension, ignoreCase: true, out var dimension))
            return Invalid($"Invalid dimension. Valid: {string.Join(", ", Enum.GetNames<RoutingDimension>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        string? territoryJson = null;

        if (strategy == RoutingStrategy.Territory)
        {
            var ruleSet = TerritoryRuleSet.Parse(request.TerritoryMapJson);

            if (ruleSet is null)
            {
                return Invalid(
                    "Territory rules could not be read.",
                    ["The advanced JSON must be an object of rules, or a map of value to user id."]);
            }

            if (dimension == RoutingDimension.CustomField && string.IsNullOrWhiteSpace(request.CustomFieldKey))
                return Invalid("Choose the custom field that drives routing.");

            var assigneeIds = await db.Users
                .Where(u => !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var errors = ruleSet.Validate(assigneeIds);
            if (errors.Count > 0)
                return Invalid("Territory rules are not valid.", errors);

            // Normalise to the structured shape so a legacy flat map is upgraded the first
            // time it is saved through this endpoint.
            territoryJson = ruleSet.ToJson();
        }

        // One routing config per tenant (D-15) — upsert
        var existing = await db.RoutingConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken);

        var customFieldKey = strategy == RoutingStrategy.Territory && dimension == RoutingDimension.CustomField
            ? request.CustomFieldKey
            : null;

        if (existing != null)
        {
            existing.Update(strategy, dimension, territoryJson, customFieldKey);
            existing.Enable();
        }
        else
        {
            existing = RoutingConfig.Create(tenantId, strategy, dimension, territoryJson, customFieldKey);
            db.RoutingConfigs.Add(existing);
        }

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(existing.Id, existing.Strategy.ToString(),
            existing.Dimension.ToString(), existing.IsEnabled));
    }

    private static BadRequest<ValidationProblem> Invalid(string message, List<string>? errors = null)
        => TypedResults.BadRequest(new ValidationProblem(message, errors ?? []));
}
