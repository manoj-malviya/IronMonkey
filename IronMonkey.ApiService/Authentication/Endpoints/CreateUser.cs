using FluentValidation;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class CreateUser : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users", Handle)
        .WithSummary("Creates a new user in the system")
        .WithRequestValidation<Request>();

    public record Request(Guid TenantId, string Name, string Email, string Password, int RoleId);
    public record Response(Guid UserId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.TenantId).NotEmpty().WithMessage("Tenant ID is required.");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Valid email is required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
            RuleFor(x => x.RoleId).GreaterThan(0).WithMessage("Role ID must be a positive integer.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(Request request, AppDbContext dbContext)
    {
        var role = await dbContext.Set<Role>().FirstOrDefaultAsync(r => r.Id == request.RoleId);

        if (role == null)
        {
            return TypedResults.NotFound();
        }

        var user = User.Create(request.TenantId, request.Name, request.Email, request.Password, role);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new Response(user.Id, "User created successfully."));
    }
}