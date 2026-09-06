using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Services;

public class PlatformAdminOptions
{
    /// <summary>Email for the seeded SuperAdmin. Seeding is skipped when unset.</summary>
    public string? Email { get; init; }

    /// <summary>
    /// Initial plaintext password, hashed before storage. Seeding is skipped when unset,
    /// so no working credential ships in source control.
    /// </summary>
    public string? Password { get; init; }
}

public interface IPlatformAdminSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates the platform SuperAdmin in the central DB at startup when configured.
/// Idempotent: an existing row with the configured email is left untouched, so a
/// rotated password is never silently reverted to the configured value on restart.
/// </summary>
public class PlatformAdminSeeder(
    CentralDbContext centralDb,
    IOptions<PlatformAdminOptions> options,
    ILogger<PlatformAdminSeeder> logger) : IPlatformAdminSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var email = options.Value.Email?.Trim();
        var password = options.Value.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "PlatformAdmin:Email/Password not configured — skipping SuperAdmin seeding. " +
                "Signup approval requires a platform admin; configure both to create one.");
            return;
        }

        var exists = await centralDb.PlatformUsers
            .AsNoTracking()
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (exists)
        {
            logger.LogInformation("Platform SuperAdmin {Email} already present.", email);
            return;
        }

        centralDb.PlatformUsers.Add(PlatformUser.Create(
            name: "Platform Admin",
            email: email,
            passwordHash: BC.HashPassword(password),
            role: RoleConstants.SuperAdmin));

        await centralDb.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded platform SuperAdmin {Email}.", email);
    }
}
