using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.UserManagement;

public class CreateTenantUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users", Handle)
        .WithSummary("Create a new user in the current tenant with auto-generated password")
        .RequireAuthorization()
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

    private static async Task<Results<Created<Response>, ValidationError, NotFound>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();

        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        var plaintext = Convert.ToBase64String(bytes);
        var hashed = BC.HashPassword(plaintext);

        var user = User.Create(tenantId, request.Name, request.Email, hashed, role);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/user-management/users/{user.Id}",
            new Response(user.Id, plaintext, "User created successfully."));
    }
}
