using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Types;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class Login : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/login", Handle)
        .WithSummary("Logs in a user")
        .WithRequestValidation<Request>();

    public record Request(string Email, string Password);
    public record Response(string Token);
    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty();
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(Request request, AppDbContext database, Jwt jwt, CancellationToken cancellationToken)
    {
        var user = await database.Users
            .Include(x => x.Roles)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
        
        if (user is null || user.Password != request.Password)
        {
            return TypedResults.Unauthorized();
        }

        var token = jwt.GenerateToken(new LoggedInUser(user.IdentityId, user.Name, user.Email, user.Roles?.First()?.Name));
        var response = new Response(token);
        return TypedResults.Ok(response);
    }
}