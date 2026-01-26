using FluentValidation;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class AttachPermissionsToRole : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/roles/{roleId}/permissions", Handle)
        .WithSummary("Attaches one or more permissions to a role")
        .WithRequestValidation<Request>();

    public record Request(List<int> PermissionIds);
    public record Response(string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.PermissionIds)
                .NotEmpty().WithMessage("Permission IDs are required.")
                .Must(ids => ids.All(id => id > 0)).WithMessage("Permission IDs must be positive integers.");
        }
    }

    private static async Task<Results<Ok<Response>, NotFound, ValidationError>> Handle(int roleId, Request request, AppDbContext dbContext)
    {
        var role = await dbContext.Set<Role>()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId);

        if (role == null)
        {
            return TypedResults.NotFound();
        }

        var permissions = await dbContext.Set<Permission>()
            .Where(p => request.PermissionIds.Contains(p.Id))
            .ToListAsync();

        if (permissions.Count != request.PermissionIds.Count)
        {
            return new ValidationError("Some permissions were not found.");
        }

        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new Response("Permissions successfully attached to the role."));
    }
}