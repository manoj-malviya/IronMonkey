using Npgsql;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Services;

public interface ITenantProvisioningService
{
    Task ProvisionTenantAsync(Guid signupRequestId, CancellationToken cancellationToken = default);
}

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly CentralDbContext _centralDb;
    private readonly ITenantDbContextFactory _tenantContextFactory;
    private readonly IConfiguration _config;

    public TenantProvisioningService(
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        IConfiguration config)
    {
        _centralDb = centralDb;
        _tenantContextFactory = tenantContextFactory;
        _config = config;
    }

    public async Task ProvisionTenantAsync(Guid signupRequestId, CancellationToken cancellationToken = default)
    {
        // Step 1: Load and validate signup request
        var signupRequest = await _centralDb.SignupRequests
            .SingleOrDefaultAsync(r => r.Id == signupRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"SignupRequest {signupRequestId} not found.");

        if (signupRequest.Status != "Approved")
            throw new InvalidOperationException("Can only provision Approved signup requests.");

        // Step 2: Create Tenant record in central DB
        var slug = GenerateSlug(signupRequest.CompanyName);
        var tenant = Tenant.Create(signupRequest.CompanyName, slug, "Standard", "Active");
        _centralDb.Tenants.Add(tenant);
        await _centralDb.SaveChangesAsync(cancellationToken);

        // Step 3: Create the PostgreSQL database
        var connectionString = await CreateTenantDatabaseAsync(slug, cancellationToken);

        // Step 4: Apply EF Core migrations to the new tenant DB
        await using var tenantDb = _tenantContextFactory.CreateForTenant(connectionString, tenant.Id);
        await tenantDb.Database.MigrateAsync(cancellationToken);

        // Step 5: Seed default data
        await SeedTenantDataAsync(tenantDb, tenant, signupRequest, cancellationToken);

        // Step 6: Mark tenant as provisioned in central DB
        tenant.MarkProvisioned(connectionString);
        signupRequest.LinkTenant(tenant.Id);
        await _centralDb.SaveChangesAsync(cancellationToken);

        // Step 7: Register admin email in central user-tenant index for login resolution
        _centralDb.UserTenantIndex.Add(UserTenantIndex.Create(signupRequest.AdminEmail, tenant.Id));
        await _centralDb.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> CreateTenantDatabaseAsync(string slug, CancellationToken cancellationToken)
    {
        var centralConnStr = _config.GetConnectionString("CentralDb")
            ?? throw new InvalidOperationException("CentralDb connection string required.");

        var dbName = $"ironmonkey_{slug.ToLower().Replace("-", "_")}";

        await using var connection = new NpgsqlConnection(centralConnStr);
        await connection.OpenAsync(cancellationToken);

        // Check if DB already exists to make provisioning idempotent
        await using var checkCmd = new NpgsqlCommand(
            $"SELECT 1 FROM pg_database WHERE datname = '{dbName}'", connection);
        var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

        if (exists is null)
        {
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Build connection string for new tenant DB using same server/credentials as central
        var builder = new NpgsqlConnectionStringBuilder(centralConnStr) { Database = dbName };
        return builder.ToString();
    }

    private static async Task SeedTenantDataAsync(
        TenantDbContext db,
        Tenant tenant,
        SignupRequest signupRequest,
        CancellationToken cancellationToken)
    {
        // Roles are already seeded by EF migration (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302)
        // Look up the Admin role from the migrated DB — do not re-insert
        var adminRole = await db.Roles
            .SingleAsync(r => r.Name == "Admin", cancellationToken);

        // Seed admin user with BCrypt-hashed password from signup request
        // Note: AdminPasswordHash in SignupRequest is already BCrypt-hashed
        var adminUser = User.Create(
            tenant.Id,
            signupRequest.AdminEmail.Split('@')[0],  // Name from email prefix
            signupRequest.AdminEmail,
            signupRequest.AdminPasswordHash,  // Already hashed — store as-is
            adminRole);
        db.Users.Add(adminUser);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateSlug(string companyName)
    {
        return companyName
            .ToLower()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Trim('-');
    }
}
