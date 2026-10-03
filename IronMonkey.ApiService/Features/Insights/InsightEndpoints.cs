using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Insights;

public sealed record QueryProblems(string Message, IReadOnlyList<string> Problems);
public sealed record ViewQueryRequest(ViewDefinition Definition, int? Page, int? PageSize);
public sealed record ReportRunRequest(string RecordType, ReportSpec Spec);

public sealed record SavedViewResponse(Guid Id, string Name, string RecordType, ViewDefinition Definition, bool IsShared,
    bool IsOwner, bool IsDefault, string OwnerName, List<string> Problems);

public sealed record SaveViewRequest(string Name, string RecordType, ViewDefinition Definition, bool IsShared);

internal static class Insights
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool TryType(string? value, out InsightRecordType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);

    public static IResult Problems(QueryValidationException ex) =>
        TypedResults.BadRequest(new QueryProblems("The filter cannot be run as written.", ex.Problems));

    public static IResult Timeout(QueryTimeoutException ex) =>
        TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status504GatewayTimeout);

    public static async Task<(TenantDbContext Db, Guid TenantId)> OpenAsync(ITenantService tenantService, ITenantDbContextFactory factory, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        return (factory.CreateForTenant(await tenantService.GetConnectionStringAsync(ct), tenantId), tenantId);
    }
}

/// <summary>Global search across every record type the caller may read.</summary>
public class SearchEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/search", Handle)
        .WithTags("Search").WithSummary("Search leads, contacts, opportunities and tasks by name, email, phone or searchable custom fields")
        .RequireAuthorization();

    /// <param name="type">Optional single type, for paging one group.</param>
    internal static async Task<IResult> Handle(string? q, string? type, int? limit, int? page,
        ITenantService tenantService, ITenantDbContextFactory factory, IUserContext user, SearchService search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < SearchService.MinQueryLength)
            return TypedResults.BadRequest($"Type at least {SearchService.MinQueryLength} characters.");

        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            // Only types the caller holds the read permission for are searched at all — a group
            // for a type they cannot read would disclose that matches exist.
            var held = await RoleManagement.PrivilegeGuard.ActorPermissionsAsync(db, user.UserId, ct);
            var types = new[] { InsightRecordType.Lead, InsightRecordType.Contact, InsightRecordType.Opportunity, InsightRecordType.Task }
                .Where(t => held.Contains(ReadPermission(t)))
                .Where(t => type is null || (Insights.TryType(type, out var only) && only == t))
                .ToList();

            return TypedResults.Ok(await search.SearchAsync(db, tenantId, q, types, limit ?? SearchService.DefaultPerType, page ?? 1, ct));
        }
    }

    internal static string ReadPermission(InsightRecordType t) => t switch
    {
        InsightRecordType.Contact => IronMonkey.Common.Auth.PermissionConstants.ContactsRead,
        InsightRecordType.Opportunity => IronMonkey.Common.Auth.PermissionConstants.OpportunitiesRead,
        _ => IronMonkey.Common.Auth.PermissionConstants.LeadsRead
    };
}

/// <summary>The field catalog and ad-hoc filtered lists that saved views and the URL drive.</summary>
public class InsightQueryEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/insights/{recordType}/fields", Fields)
            .WithTags("Views").WithSummary("Fields, kinds and operators available to filters, columns, sorts and reports")
            .RequireAuthorization();

        app.MapPost("/api/insights/{recordType}/query", Query)
            .WithTags("Views").WithSummary("Run a filter/columns/sort definition and return one page")
            .RequireAuthorization();
    }

    internal static async Task<IResult> Fields(string recordType, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        if (!Insights.TryType(recordType, out var type)) return TypedResults.NotFound();
        var (db, _) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();
            return TypedResults.Ok(InsightService.Describe(record));
        }
    }

    internal static async Task<IResult> Query(string recordType, ViewQueryRequest request, ITenantService tenantService,
        ITenantDbContextFactory factory, IUserContext user, InsightService insights, CancellationToken ct)
    {
        if (!Insights.TryType(recordType, out var type)) return TypedResults.NotFound();
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();

            try
            {
                var context = await insights.ContextAsync(db, tenantId, user.UserId, ct);
                return TypedResults.Ok(await insights.RunViewAsync(db, record, context, request.Definition,
                    request.Page ?? 1, request.PageSize ?? 25, ct));
            }
            catch (QueryValidationException ex) { return Insights.Problems(ex); }
            catch (QueryTimeoutException ex) { return Insights.Timeout(ex); }
        }
    }
}

/// <summary>
/// Saved views. A user sees their own views and every shared view for record types they may
/// read; only the owner edits or deletes. Running a view always applies the runner's own
/// visibility, so sharing a view never shares records.
/// </summary>
public class SavedViewEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/views", List).WithTags("Views").RequireAuthorization();
        app.MapPost("/api/views", Create).WithTags("Views").RequireAuthorization();
        app.MapPut("/api/views/{id:guid}", Update).WithTags("Views").RequireAuthorization();
        app.MapDelete("/api/views/{id:guid}", Delete).WithTags("Views").RequireAuthorization();
        app.MapPut("/api/views/{id:guid}/default", SetDefault).WithTags("Views").RequireAuthorization();
        app.MapDelete("/api/views/default/{recordType}", ClearDefault).WithTags("Views").RequireAuthorization();
    }

    internal static async Task<IResult> List(string recordType, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        if (!Insights.TryType(recordType, out var type)) return TypedResults.NotFound();
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();

            var views = await db.SavedViews.AsNoTracking()
                .Where(v => v.RecordType == type && (v.OwnerUserId == user.UserId || v.IsShared))
                .OrderBy(v => v.Name).ThenBy(v => v.Id)
                .ToListAsync(ct);

            var defaultId = await db.UserDefaultViews.Where(d => d.UserId == user.UserId && d.RecordType == type)
                .Select(d => (Guid?)d.SavedViewId).FirstOrDefaultAsync(ct);
            var owners = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId)
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

            return TypedResults.Ok(views.Select(v => ToResponse(v, record, user.UserId, defaultId, owners, tenantId)).ToList());
        }
    }

    internal static async Task<IResult> Create(SaveViewRequest request, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, CancellationToken ct)
    {
        if (!Insights.TryType(request.RecordType, out var type)) return TypedResults.BadRequest("Unknown record type.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100) return TypedResults.BadRequest("A view needs a name of up to 100 characters.");

        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();

            var problems = new QueryCompiler(record, await insights.ContextAsync(db, tenantId, user.UserId, ct)).Validate(request.Definition);
            if (problems.Count > 0) return TypedResults.BadRequest(new QueryProblems("The view cannot be saved as written.", problems));

            var view = SavedView.Create(tenantId, user.UserId, request.Name, type, JsonSerializer.Serialize(request.Definition, Insights.Json), request.IsShared);
            db.SavedViews.Add(view);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/views/{view.Id}", new { view.Id });
        }
    }

    internal static async Task<IResult> Update(Guid id, SaveViewRequest request, ITenantService tenantService,
        ITenantDbContextFactory factory, IUserContext user, InsightService insights, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id, ct);
            // Another user's view — shared or not — is not theirs to change, and a private one
            // must not even be confirmed to exist.
            if (view is null || view.OwnerUserId != user.UserId) return TypedResults.NotFound();
            if (string.IsNullOrWhiteSpace(request.Name)) return TypedResults.BadRequest("A view needs a name.");

            var record = await RecordCatalog.LoadAsync(db, view.RecordType, ct);
            var problems = new QueryCompiler(record, await insights.ContextAsync(db, tenantId, user.UserId, ct)).Validate(request.Definition);
            if (problems.Count > 0) return TypedResults.BadRequest(new QueryProblems("The view cannot be saved as written.", problems));

            view.Update(request.Name, JsonSerializer.Serialize(request.Definition, Insights.Json), request.IsShared);
            view.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
    }

    internal static async Task<IResult> Delete(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        var (db, _) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id, ct);
            if (view is null || view.OwnerUserId != user.UserId) return TypedResults.NotFound();
            db.UserDefaultViews.RemoveRange(await db.UserDefaultViews.Where(d => d.SavedViewId == id).ToListAsync(ct));
            view.IsDeleted = true;
            view.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
    }

    internal static async Task<IResult> SetDefault(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id && (v.OwnerUserId == user.UserId || v.IsShared), ct);
            if (view is null) return TypedResults.NotFound();

            var existing = await db.UserDefaultViews.SingleOrDefaultAsync(d => d.UserId == user.UserId && d.RecordType == view.RecordType, ct);
            if (existing is null) db.UserDefaultViews.Add(UserDefaultView.Create(tenantId, user.UserId, view.RecordType, view.Id));
            else existing.Point(view.Id);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
    }

    internal static async Task<IResult> ClearDefault(string recordType, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        if (!Insights.TryType(recordType, out var type)) return TypedResults.NotFound();
        var (db, _) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            db.UserDefaultViews.RemoveRange(await db.UserDefaultViews.Where(d => d.UserId == user.UserId && d.RecordType == type).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
    }

    /// <summary>
    /// A stored definition is re-validated on every read, so a view whose custom field was
    /// deleted reports itself broken ("the custom field … no longer exists") instead of quietly
    /// returning nothing — an empty result and a broken filter must look different.
    /// </summary>
    private static SavedViewResponse ToResponse(SavedView v, RecordDef record, Guid userId, Guid? defaultId,
        Dictionary<Guid, string> owners, Guid tenantId)
    {
        var definition = JsonSerializer.Deserialize<ViewDefinition>(v.DefinitionJson, Insights.Json) ?? new ViewDefinition();
        var problems = new QueryCompiler(record, new QueryContext(tenantId, userId, Data.Visibility.RecordVisibility.Unrestricted,
            TimeZoneInfo.Utc, DateTime.UtcNow)).Validate(definition).ToList();
        return new SavedViewResponse(v.Id, v.Name, v.RecordType.ToString(), definition, v.IsShared, v.OwnerUserId == userId,
            v.Id == defaultId, owners.GetValueOrDefault(v.OwnerUserId, "—"), problems);
    }
}
