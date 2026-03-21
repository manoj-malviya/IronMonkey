using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class CreateRole : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/roles", Handle)
        .WithSummary("Creates a new role in the system")
        .WithRequestValidation<Request>();

    public record Request(string RoleName);
    public record Response(string RoleId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.RoleName).NotEmpty().WithMessage("Role name is required.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle(Request request, AppDbContext dbContext)
    {
        var isRoleExists = await dbContext.Set<Role>()
            .AnyAsync(role => role.Name == request.RoleName);

        if (isRoleExists)
        {
            return new ValidationError("Role already exists.");
        }

        var role = Role.Create(0, request.RoleName);
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new Response(role.Id.ToString(), "Role created successfully."));
    }
}
