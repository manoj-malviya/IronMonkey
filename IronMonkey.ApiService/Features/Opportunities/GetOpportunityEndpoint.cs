using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities;

public class GetOpportunityEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities/{id:guid}", Handle)
        .WithSummary("Get a single opportunity by id")
        .WithTags("Opportunities")
        .RequireAuthorization();

    /// <param name="Stage">The stage's current name, for display only.</param>
    /// <param name="StageType">
    /// Won/lost is read from here, never from the name — the tenant may rename any stage.
    /// </param>
    public record Response(
        Guid Id, string Title, Guid ContactId, string ContactName,
        Guid StageId, string Stage, string StageType, bool IsTerminal,
        decimal Amount, DateTime ExpectedCloseDate,
        string? LossReason, DateTime CreatedAt,
        // Amount is computed from line items; these split it and say what currency it is in
        // (null = the tenant's own). See /api/opportunities/{id}/lines for the lines.
        decimal OneOffAmount = 0, decimal RecurringAmount = 0, string? CurrencyCode = null,
        decimal? ExchangeRate = null, int LineCount = 0);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await db.Opportunities
            .Include(o => o.Contact)
            .Include(o => o.Stage)
            .Include(o => o.LineItems)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (opportunity is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new Response(
            opportunity.Id, opportunity.Title, opportunity.ContactId,
            opportunity.Contact?.Name ?? "—",
            opportunity.PipelineStageId,
            opportunity.Stage?.Name ?? "—",
            (opportunity.Stage?.StageType ?? StageType.Active).ToString(),
            opportunity.Stage?.IsTerminal ?? false,
            opportunity.Amount,
            opportunity.ExpectedCloseDate, opportunity.LossReason, opportunity.CreatedAt,
            opportunity.OneOffAmount, opportunity.RecurringAmount, opportunity.CurrencyCode,
            opportunity.ExchangeRate, opportunity.LineItems.Count));
    }
}
