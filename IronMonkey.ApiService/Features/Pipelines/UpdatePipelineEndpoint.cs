using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Pipelines;

public class UpdatePipelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/pipelines/{id:guid}", Handle)
        .WithSummary("Rename a pipeline, change its description, default flag or active state")
        .WithTags("Pipelines")
        .RequireAuthorization();

    /// <param name="IsDefault">
    /// Optional. Passing true promotes this pipeline and demotes the current default in the
    /// same transaction. Passing false is refused on the current default — clearing it would
    /// leave the record type with no default, and "no pipeline specified" with no answer.
    /// </param>
    /// <param name="IsActive">Optional. Deactivating is refused while records remain.</param>
    public record Request(
        string Name, string? Description = null, bool? IsDefault = null, bool? IsActive = null);

    public record Response(Guid Id, string Name, string RecordType, bool IsDefault, bool IsActive);

    internal static async Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Pipeline name must be between 1 and 100 characters.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var pipeline = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pipeline is null) return TypedResults.NotFound();

        var nameExists = await db.Pipelines.AnyAsync(
            p => p.Id != id && p.RecordType == pipeline.RecordType && p.Name.ToLower() == name.ToLower(),
            cancellationToken);

        if (nameExists)
            return TypedResults.Conflict($"A pipeline named '{name}' already exists for this record type.");

        if (request.IsDefault is false && pipeline.IsDefault)
        {
            return TypedResults.Conflict(
                "A record type must always have a default pipeline. Make another pipeline the default instead.");
        }

        if (request.IsActive is false && pipeline.IsActive)
        {
            if (pipeline.IsDefault)
            {
                return TypedResults.Conflict(
                    "The default pipeline cannot be deactivated. Make another pipeline the default first.");
            }

            // Deactivating hides the pipeline from every picker. Records left inside it
            // would be reachable from no board and no filter — the same trap the stage
            // endpoints refuse, for the same reason.
            var recordCount = pipeline.RecordType == Data.Entities.PipelineRecordType.Lead
                ? await db.Leads.CountAsync(l => l.PipelineId == id, cancellationToken)
                : await db.Opportunities.CountAsync(o => o.PipelineId == id, cancellationToken);

            if (recordCount > 0)
            {
                return TypedResults.Conflict(
                    $"'{pipeline.Name}' still holds {recordCount} record(s). " +
                    "Move them to another pipeline before deactivating it.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (request.IsDefault is true && !pipeline.IsDefault)
        {
            if (!pipeline.IsActive)
            {
                return TypedResults.Conflict(
                    "An inactive pipeline cannot be made the default. Reactivate it first.");
            }

            var previous = await db.Pipelines
                .Where(p => p.RecordType == pipeline.RecordType && p.IsDefault && p.Id != id)
                .ToListAsync(cancellationToken);

            foreach (var p in previous) p.SetDefault(false);
            pipeline.SetDefault(true);
        }

        pipeline.Rename(name);
        pipeline.SetDescription(request.Description?.Trim());
        if (request.IsActive.HasValue) pipeline.SetActive(request.IsActive.Value);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            pipeline.Id, pipeline.Name, pipeline.RecordType.ToString(),
            pipeline.IsDefault, pipeline.IsActive));
    }
}
