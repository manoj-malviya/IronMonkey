using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Types;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ConfirmEmail : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/confirmEmail", Handle)
        .WithRequestValidation<Request>()
        .WithSummary("Confirm Email Address");

    public record Request(string userId, string code);
    public record Response(string Token);
    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.userId).NotEmpty();
            RuleFor(x => x.code).NotEmpty();
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle([AsParameters] Request request, AppDbContext db, Jwt jwt, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>()
            .FirstAsync(x => x.IdentityId == request.userId, cancellationToken);
        
        if (user == null)
        {
            return new ValidationError("User not found");
        }

        // var result = await userManager.ConfirmEmailAsync(user, request.code);
        //
        // if (!result.Succeeded)
        // {
        //     return new ValidationError("Failed to confirm email");
        // }

        return TypedResults.Ok<Response>(new Response(jwt.GenerateToken(new LoggedInUser(user.IdentityId, user.Name, user.Email, user.Roles?.First()?.ToString()))));

        // return TypedResults.Ok<Response>(new Response(""));
    }
}