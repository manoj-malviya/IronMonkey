using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class LoginEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/auth/login", Handle)
        .WithSummary("Authenticate user and receive JWT with TenantId claim")
        .WithTags("Authentication")
        .AllowAnonymous()
        .WithRequestValidation<Request>();

    public record Request(string Email, string Password);
    public record Response(string Token, Guid TenantId);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    // Test-accessible handler — same logic as Handle, exposed for integration testing
    internal static Task<Results<Ok<Response>, ValidationError, UnauthorizedHttpResult>> HandleForTest(
        Request request,
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        Jwt jwt,
        CancellationToken cancellationToken)
        => Handle(request, centralDb, tenantContextFactory, jwt, cancellationToken);

    private static async Task<Results<Ok<Response>, ValidationError, UnauthorizedHttpResult>> Handle(
        Request request,
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        Jwt jwt,
        CancellationToken cancellationToken)
    {
        // Step 0: Platform operators live in the central DB and belong to no tenant,
        // so they are resolved before the tenant lookup. Their token carries an empty
        // tenant_id — they administer tenants rather than being a member of one.
        var platformUser = await centralDb.PlatformUsers
            .SingleOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (platformUser is not null)
        {
            if (!BC.Verify(request.Password, platformUser.PasswordHash))
                return TypedResults.Unauthorized();

            platformUser.RecordLogin();
            await centralDb.SaveChangesAsync(cancellationToken);

            var platformToken = jwt.GenerateToken(new LoggedInUser(
                platformUser.Id.ToString(),
                platformUser.Name,
                platformUser.Email,
                platformUser.Role,
                Guid.Empty));

            return TypedResults.Ok(new Response(platformToken, Guid.Empty));
        }

        // Step 1: Find tenant from central email index
        var emailIndex = await centralDb.UserTenantIndex
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == request.Email, cancellationToken);

        if (emailIndex is null)
            return TypedResults.Unauthorized();

        // Step 2: Find the tenant and verify it's provisioned
        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == emailIndex.TenantId, cancellationToken);

        if (tenant is null || !tenant.IsProvisioned || tenant.DatabaseConnectionString is null)
            return TypedResults.Unauthorized();

        // Step 3: Open tenant DB and verify password
        await using var tenantDb = tenantContextFactory.CreateForTenant(
            tenant.DatabaseConnectionString, tenant.Id);

        var user = await tenantDb.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null)
            return TypedResults.Unauthorized();

        // Password verification using BCrypt
        if (!BC.Verify(request.Password, user.Password))
            return TypedResults.Unauthorized();

        // Identity must be the primary key, not IdentityId: nothing populates IdentityId
        // during provisioning, so it is "" for every tenant user. Keying authorization on
        // it would match every user in the tenant and union their permissions together.
        var loggedInUser = new LoggedInUser(
            user.Id.ToString(),
            user.Name,
            user.Email,
            user.Roles.FirstOrDefault()?.Name ?? "User",
            tenant.Id
        );

        var token = jwt.GenerateToken(loggedInUser);
        return TypedResults.Ok(new Response(token, tenant.Id));
    }
}
