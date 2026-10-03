using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.RoleManagement;

public sealed record SetOwnerRequest(Guid? OwnerUserId);
public sealed record OwnerResponse(Guid Id, Guid? OwnerUserId);
public sealed record AssignableUser(Guid Id, string Name);

/// <summary>
/// Reassigns a contact or opportunity. The record must be visible to the caller (it is loaded
/// through the filtered context, so an invisible one is a 404); the new owner need not be —
/// handing a record to a colleague whose book the caller cannot see is exactly what managers
/// and routing do. After the change the caller may no longer see the record, which is the
/// intended outcome of giving it away, not data loss.
/// </summary>
public static class RecordOwnerEndpoints
{
    public static void MapContact(IEndpointRouteBuilder app) => app
        .MapPut("/api/contacts/{id:guid}/owner", SetContactOwner)
        .WithTags("Contacts").WithSummary("Change a contact's owner")
        .RequireAuthorization();

    public static void MapOpportunity(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunities/{id:guid}/owner", SetOpportunityOwner)
        .WithTags("Opportunities").WithSummary("Change an opportunity's owner")
        .RequireAuthorization();

    internal static async Task<Results<Ok<OwnerResponse>, ValidationError, NotFound>> SetContactOwner(Guid id, SetOwnerRequest request,
        ITenantService tenantService, ITenantDbContextFactory factory, CancellationToken ct)
    {
        await using var db = factory.CreateForTenant(await tenantService.GetConnectionStringAsync(ct), tenantService.GetCurrentTenantId());
        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null) return TypedResults.NotFound();
        if (await ValidateOwnerAsync(db, request.OwnerUserId, ct) is { } error) return new ValidationError(error);

        contact.AssignOwner(request.OwnerUserId);
        contact.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new OwnerResponse(contact.Id, contact.OwnerUserId));
    }

    internal static async Task<Results<Ok<OwnerResponse>, ValidationError, NotFound>> SetOpportunityOwner(Guid id, SetOwnerRequest request,
        ITenantService tenantService, ITenantDbContextFactory factory, CancellationToken ct)
    {
        await using var db = factory.CreateForTenant(await tenantService.GetConnectionStringAsync(ct), tenantService.GetCurrentTenantId());
        var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return TypedResults.NotFound();
        if (await ValidateOwnerAsync(db, request.OwnerUserId, ct) is { } error) return new ValidationError(error);

        opportunity.AssignOwner(request.OwnerUserId);
        opportunity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new OwnerResponse(opportunity.Id, opportunity.OwnerUserId));
    }

    /// <summary>Owners must be active users of this tenant. Users are never visibility-filtered.</summary>
    internal static async Task<string?> ValidateOwnerAsync(TenantDbContext db, Guid? ownerUserId, CancellationToken ct) =>
        ownerUserId is { } owner && !await db.Users.AnyAsync(u => u.Id == owner, ct)
            ? "That user does not exist or is deactivated."
            : null;
}

/// <summary>
/// Who a record can be assigned to: every active tenant user, by id and name only. Open to any
/// tenant user — unlike GET /users, which needs users:read — because assigning and routing must
/// be able to target users whose records the caller cannot see, under any scope.
/// </summary>
public static class AssignableUsersEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/users/assignable", Handle)
        .WithTags("User Management").WithSummary("Active users records can be assigned to")
        .RequireAuthorization();

    internal static async Task<Ok<List<AssignableUser>>> Handle(ITenantService tenantService, ITenantDbContextFactory factory, CancellationToken ct)
    {
        await using var db = factory.CreateForTenant(await tenantService.GetConnectionStringAsync(ct), tenantService.GetCurrentTenantId());
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Name).ThenBy(u => u.Id)
            .Select(u => new AssignableUser(u.Id, u.Name)).ToListAsync(ct);
        return TypedResults.Ok(users);
    }
}
