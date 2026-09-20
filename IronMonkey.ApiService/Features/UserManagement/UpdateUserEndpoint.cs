using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement;

public class UpdateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/users/{id:guid}", Handle)
        .WithSummary("Update a user's name, email, and role")
        .RequireAuthorization()
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

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
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

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();

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
        await db.SaveChangesAsync(cancellationToken);

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
