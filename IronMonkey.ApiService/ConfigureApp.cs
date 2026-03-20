using Microsoft.EntityFrameworkCore;
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
        app.MapEndpoints();
        
        await app.EnsureDatabaseCreated();
    }

    private static async Task EnsureDatabaseCreated(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var centralDb = scope.ServiceProvider.GetRequiredService<CentralDbContext>();
        await centralDb.Database.MigrateAsync();
    }
}