using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement;

public class UpdateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/users/{id:guid}", Handle)
        .WithSummary("Update a user's name, email, and role")
        .RequireAuthorization(PermissionConstants.UsersWrite)
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, int RoleId);
    public record Response(Guid UserId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
            RuleFor(x => x.RoleId).GreaterThan(0).WithMessage("A valid role ID is required.");
        }
    }

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        AuthorizationService authorizationService,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        var email = request.Email.Trim().ToLowerInvariant();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var user = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Id == id)
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return TypedResults.NotFound();

        // SuperAdmin is the platform operator's role and is filtered out of every
        // tenant-facing path. A tenant that could assign it would be minting itself a
        // platform administrator with admin:access over every other tenant.
        if (!TenantRoleRules.IsVisibleToTenant(request.RoleId))
            return TypedResults.NotFound();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();

        var previousRole = user.Roles.FirstOrDefault();
        var roleChanged = previousRole?.Id != request.RoleId;

        // Demoting the last Admin locks the tenant out of its own administration just as
        // surely as deactivating them, so the same guard applies to a role change away
        // from Admin. Moving an Admin to Admin is a no-op and is not blocked.
        if (roleChanged &&
            request.RoleId != AdminSafetyGuard.AdminRoleId &&
            await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, id, cancellationToken))
        {
            return new ValidationError(AdminSafetyGuard.LastAdminMessage);
        }

        // The central index is keyed on email, so an email change has to move with it —
        // otherwise login keeps resolving the old address and the new one 401s.
        var previousEmail = user.Email;
        var emailChanged = !string.Equals(previousEmail, email, StringComparison.OrdinalIgnoreCase);

        if (emailChanged)
        {
            var emailTaken = await centralDb.UserTenantIndex
                .AsNoTracking()
                .AnyAsync(x => x.Email == email, cancellationToken);

            if (emailTaken)
                return new ValidationError("A user with this email address already exists.");
        }

        user.Update(request.Name, email);
        user.UpdateRole(role);

        // The JWT carries no permission claims — AuthorizationService resolves them from
        // role_permissions — but it caches the result for five minutes. Without the
        // invalidation below, a demotion would leave the old, higher permissions live for
        // up to that long. Dropping the entry makes the change effective on the user's very
        // next authenticated request, with no re-login needed.
        if (roleChanged)
        {
            db.UserAuditLogs.Add(UserAuditLog.Record(
                tenantId,
                UserAuditEvent.RoleChanged,
                email,
                DateTime.UtcNow,
                targetUserId: user.Id,
                actorUserId: userContext.UserId,
                detail: $"{previousRole?.Name ?? "None"} -> {role.Name}"));
        }
        else
        {
            db.UserAuditLogs.Add(UserAuditLog.Record(
                tenantId,
                UserAuditEvent.ProfileUpdated,
                email,
                DateTime.UtcNow,
                targetUserId: user.Id,
                actorUserId: userContext.UserId,
                detail: emailChanged ? $"Email changed from {previousEmail}" : null));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (roleChanged)
            await authorizationService.InvalidatePermissionsAsync(user.Id, tenantId, cancellationToken);

        if (emailChanged)
        {
            var indexRows = await centralDb.UserTenantIndex
                .Where(x => x.Email == previousEmail && x.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            centralDb.UserTenantIndex.RemoveRange(indexRows);
            centralDb.UserTenantIndex.Add(UserTenantIndex.Create(email, tenantId));
            await centralDb.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new Response(user.Id, "User updated successfully."));
    }
}
