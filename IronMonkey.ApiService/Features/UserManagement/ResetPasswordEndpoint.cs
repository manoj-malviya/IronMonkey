using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.UserManagement;

public class ResetPasswordEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users/{id:guid}/reset-password", Handle)
        .WithSummary("Reset a user's password and return the new generated password")
        .RequireAuthorization();

    public record Response(string GeneratedPassword, string Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
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
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return TypedResults.NotFound();

        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        var plaintext = Convert.ToBase64String(bytes);
        var hashed = BC.HashPassword(plaintext);

        user.ResetPassword(hashed);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(plaintext, "Password reset successfully."));
    }
}
