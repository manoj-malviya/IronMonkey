using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

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
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

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

        user.Update(request.Name, request.Email);
        user.UpdateRole(role);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(user.Id, "User updated successfully."));
    }
}
