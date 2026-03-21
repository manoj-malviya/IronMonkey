using FluentValidation;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class CreatePermission : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/permissions", Handle)
        .WithSummary("Creates a new permission in the system")
        .WithRequestValidation<Request>();

    public record Request(string PermissionName);
    public record Response(int PermissionId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.PermissionName).NotEmpty().WithMessage("Permission name is required.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle(Request request, AppDbContext dbContext)
    {
        var isPermissionExists = await dbContext.Set<Permission>()
            .AnyAsync(permission => permission.Name == request.PermissionName);

        if (isPermissionExists)
        {
            return new ValidationError("Permission already exists.");
        }

        var permission = Permission.Create(0, request.PermissionName);
        dbContext.Set<Permission>().Add(permission);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new Response(permission.Id, "Permission created successfully."));
    }
}