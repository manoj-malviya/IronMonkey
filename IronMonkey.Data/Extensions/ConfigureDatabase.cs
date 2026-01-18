using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IronMonkey.Data.Types;

namespace IronMonkey.Data.Extensions;

public static class ConfigureDatabase
{
    public static void ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            var conn = configuration.GetConnectionString("DefaultConnection");
            options.UseSqlite(conn);
        });
    }
}