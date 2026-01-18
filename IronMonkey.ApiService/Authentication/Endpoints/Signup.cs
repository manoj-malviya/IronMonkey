using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Types;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class Signup : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/signup", Handle)
        .WithSummary("Creates a new user account")
        .WithRequestValidation<Request>();
        
    public record Request(string Email, string Password, string Name, string Role);
    public record Response(string Token, string Message);
    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty();
            RuleFor(x => x.Password).NotEmpty();
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Role)
                .NotEmpty()
                .Must(role => role == "Writer" || role == "Publisher")
                .WithMessage("Role must be either 'Writer' or 'Publisher'");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle(Request request, AppDbContext db, Jwt jwt, CancellationToken cancellationToken)
    {
        var isUsernameTaken = await db.Users
            .AnyAsync(x => x.Email == request.Email, cancellationToken);

        if (isUsernameTaken)
        {
            return new ValidationError("Username is already taken");
        }

        var role = request.Role == "Writer" ? Role.Writer : Role.Publisher;
        var user = User.Create(request.Name, request.Email, request.Password, role);
        user.SetIdentityId(user.Id.ToString());
        foreach (Role userRole in user.Roles)
        {
            db.Attach(userRole);
        }
        await db.Users.AddAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var loggedUser = new LoggedInUser(user.IdentityId, user.Name, user.Email, user.Roles?.First()?.ToString());
        
        var token = jwt.GenerateToken(loggedUser);
        return TypedResults.Ok(new Response(token, "User registered successfully. Please check your email to confirm your account." ));
    }
}