using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Kanban;

public class GetKanbanBoardEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/kanban", Handle)
        .WithSummary("Get Kanban board: leads grouped by pipeline stage with virtual scroll (20/column)")
        .WithTags("Pipeline")
        .RequireAuthorization();

    public record LeadCardDto(
        Guid Id, string FirstName, string LastName, string Email, string Mobile,
        string Source, Guid? AssignedToUserId, DateTime CreatedAt, int DaysInStage);

    public record KanbanColumnDto(
        Guid StageId, string StageName, int Order, string StageType,
        List<LeadCardDto> Leads, bool HasMore, int TotalCount);

    public record BoardResponse(List<KanbanColumnDto> Columns);

    private static async Task<Ok<BoardResponse>> Handle(
        Guid? assignedAgentId,
        string? leadSource,
        DateTime? dateFrom,
        DateTime? dateTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stages = await db.PipelineStages
            .Where(p => p.IsActive)
            .OrderBy(p => p.Order)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var columns = new List<KanbanColumnDto>();

        foreach (var stage in stages)
        {
            var query = db.Leads.Where(l => l.PipelineStageId == stage.Id);

            // Apply filters per D-04
            if (assignedAgentId.HasValue)
                query = query.Where(l => l.AssignedToUserId == assignedAgentId.Value);

            if (!string.IsNullOrEmpty(leadSource) && Enum.TryParse<LeadSource>(leadSource, ignoreCase: true, out var src))
                query = query.Where(l => l.Source == src);

            if (dateFrom.HasValue)
                query = query.Where(l => l.CreatedAt >= dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where(l => l.CreatedAt <= dateTo.Value);

            var totalCount = await query.CountAsync(cancellationToken);

            // Virtual scroll: first 20 per column (D-03)
            var leads = await query
                .OrderBy(l => l.CreatedAt)
                .Take(20)
                .Select(l => new LeadCardDto(
                    l.Id, l.FirstName, l.LastName, l.Email, l.Mobile,
                    l.Source.ToString(),
                    l.AssignedToUserId,
                    l.CreatedAt,
                    (int)(now - l.CreatedAt).TotalDays))
                .ToListAsync(cancellationToken);

            columns.Add(new KanbanColumnDto(
                stage.Id, stage.Name, stage.Order, stage.StageType.ToString(),
                leads, totalCount > 20, totalCount));
        }

        return TypedResults.Ok(new BoardResponse(columns));
    }
}
