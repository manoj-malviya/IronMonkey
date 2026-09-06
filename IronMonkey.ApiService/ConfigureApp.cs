using Hangfire;
using Hangfire.Dashboard;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;
using Npgsql;
using Serilog;

namespace IronMonkey.ApiService;

public static class ConfigureApp
{
    public static async Task Configure(this WebApplication app)
    {
        app.UseSerilogRequestLogging();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapEndpoints();

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new HangfireAdminOnlyAuthFilter()],
            IsReadOnlyFunc = ctx => false
        });

        // Database preparation and Hangfire job scheduling both need the central
        // database to exist, but they must not block Kestrel from binding: Aspire
        // gates dependent resources on /health responding, and creating the database
        // plus applying migrations takes long enough that a blocking startup leaves
        // the health check unanswerable and the gate never opens.
        app.Lifetime.ApplicationStarted.Register(() => _ = InitializeDatabaseAsync(app));
    }

    private static async Task InitializeDatabaseAsync(WebApplication app)
    {
        try
        {
            await app.EnsureDatabaseCreated();

            await app.SeedPlatformAdmin();

            // Must follow migration — Hangfire shares the central database, so
            // scheduling against a database that does not exist yet throws 3D000.
            RecurringJob.AddOrUpdate<TimeElapsedRuleScanJob>(
                "time-elapsed-rule-scan",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Hourly);

            app.Services.GetRequiredService<DatabaseReadinessState>().MarkReady();
            app.Logger.LogInformation("Central database ready.");
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "Database initialization failed; shutting down.");
            app.Lifetime.StopApplication();
        }
    }

    private static async Task SeedPlatformAdmin(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IPlatformAdminSeeder>();
        await seeder.SeedAsync();
    }

    private static async Task EnsureDatabaseCreated(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var centralDb = scope.ServiceProvider.GetRequiredService<CentralDbContext>();

        // Aspire's AddDatabase() only registers the connection string; it does not
        // issue CREATE DATABASE. On a fresh container the database is absent, and
        // MigrateAsync() cannot create it, so create it here before migrating.
        await EnsureDatabaseExists(centralDb.Database.GetConnectionString()!, app.Logger);

        await centralDb.Database.MigrateAsync();
    }

    private static async Task EnsureDatabaseExists(string connectionString, Microsoft.Extensions.Logging.ILogger logger)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database
            ?? throw new InvalidOperationException("Connection string has no database name.");

        // Connect to the server's default database to check for our target.
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
        check.Parameters.AddWithValue("name", databaseName);

        if (await check.ExecuteScalarAsync() is not null)
            return;

        await using var create = connection.CreateCommand();
        // Identifiers cannot be parameterized; quote to guard the name.
        create.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\"";
        await create.ExecuteNonQueryAsync();

        logger.LogInformation("Created database {DatabaseName}", databaseName);
    }
}

/// <summary>
/// Restricts the Hangfire dashboard to authenticated users with Admin or SuperAdmin role.
/// </summary>
public class HangfireAdminOnlyAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && (httpContext.User.IsInRole("Admin") || httpContext.User.IsInRole("SuperAdmin"));
    }
}
