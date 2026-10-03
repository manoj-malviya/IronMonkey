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

    /// <param name="ScopeLabel">Which pipeline the columns belong to — a name, or "All pipelines".</param>
    public record BoardResponse(
        List<KanbanColumnDto> Columns,
        Guid? PipelineId,
        string ScopeLabel,
        bool IsTenantWide,
        bool IsMultiPipeline);

    /// <param name="pipelineId">
    /// Which pipeline's board to show. Omitted means the default one — a board is a single
    /// funnel by definition, and merging two pipelines' columns would produce a board whose
    /// columns no single record can move between.
    /// </param>
    internal static async Task<Results<Ok<BoardResponse>, BadRequest<string>>> Handle(
        Guid? assignedAgentId,
        string? leadSource,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        Features.Pipelines.IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Lead, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        // The RecordType predicate also closes a pre-existing hole: opportunity stages have
        // shared this table since Part A, so an unfiltered board rendered every deal stage as
        // an empty lead column.
        var stages = await db.PipelineStages
            .Where(p => p.IsActive
                        && p.RecordType == PipelineRecordType.Lead
                        && scopedPipelines.Contains(p.PipelineId))
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Id)
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

        return TypedResults.Ok(new BoardResponse(
            columns,
            scope.SelectedPipelineId,
            scope.ScopeLabel,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant));
    }
}
