using System.Security.Cryptography;
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
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.UserManagement;

public class CreateTenantUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users", Handle)
        // Kept for backward compatibility. The invitation flow (POST /users/invitations)
        // is the primary path: it never has an admin choose or transmit someone else's
        // password, which this endpoint unavoidably does.
        .WithSummary("Create a new user in the current tenant with auto-generated password (legacy; prefer invitations)")
        .RequireAuthorization(PermissionConstants.UsersWrite)
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, int RoleId);
    public record Response(Guid UserId, string GeneratedPassword, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
            RuleFor(x => x.RoleId).GreaterThan(0).WithMessage("A valid role ID is required.");
        }
    }

    internal static async Task<Results<Created<Response>, ValidationError, NotFound>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        // Emails are the login identifier, so they are compared case-insensitively and
        // stored normalised — otherwise "A@b.com" and "a@b.com" become two users that
        // both try to claim the same central index row.
        var email = request.Email.Trim().ToLowerInvariant();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // SuperAdmin is the platform operator's role; a tenant that could assign it would be
        // minting itself a platform administrator.
        if (!TenantRoleRules.IsVisibleToTenant(request.RoleId))
            return TypedResults.NotFound();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();

        // The email must be unique across the whole platform, not just this tenant:
        // UserTenantIndex maps one email to exactly one tenant, and LoginEndpoint reads it
        // with SingleOrDefault, so a second row for the same email breaks login for both.
        var emailTaken = await centralDb.UserTenantIndex
            .AsNoTracking()
            .AnyAsync(x => x.Email == email, cancellationToken);

        if (emailTaken)
            return new ValidationError("A user with this email address already exists.");

        // Deactivated users keep their row (soft delete), so ignore the filter here —
        // reusing their email would collide on the central index.
        var existsInTenant = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken);

        if (existsInTenant)
            return new ValidationError("A user with this email address already exists.");

        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        var plaintext = Convert.ToBase64String(bytes);
        var hashed = BC.HashPassword(plaintext);

        var user = User.Create(tenantId, request.Name, email, hashed, role);
        db.Users.Add(user);

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.UserCreated,
            email,
            DateTime.UtcNow,
            targetUserId: user.Id,
            actorUserId: userContext.UserId,
            detail: $"Created directly as {role.Name}"));

        await db.SaveChangesAsync(cancellationToken);

        // Without this row LoginEndpoint's email -> tenant lookup finds nothing and the
        // brand-new user gets "Invalid email address or password". The two writes span two
        // databases and cannot share a transaction; the tenant row is written first so a
        // failure here leaves an unusable user rather than an index pointing at nothing.
        centralDb.UserTenantIndex.Add(UserTenantIndex.Create(email, tenantId));
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/users/{user.Id}",
            new Response(user.Id, plaintext, "User created successfully."));
    }
}
