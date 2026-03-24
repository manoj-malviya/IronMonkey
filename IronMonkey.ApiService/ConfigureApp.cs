using Hangfire;
using Hangfire.Dashboard;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.Data;
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

        // D-11: Hourly scan for time-elapsed workflow rules
        RecurringJob.AddOrUpdate<TimeElapsedRuleScanJob>(
            "time-elapsed-rule-scan",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly);

        await app.EnsureDatabaseCreated();
    }

    private static async Task EnsureDatabaseCreated(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var centralDb = scope.ServiceProvider.GetRequiredService<CentralDbContext>();
        await centralDb.Database.MigrateAsync();
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
