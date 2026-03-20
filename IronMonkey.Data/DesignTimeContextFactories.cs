using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IronMonkey.Data;

public class CentralDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CentralDbContext>
{
    public CentralDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ironmonkey_central;Username=postgres;Password=postgres")
            .Options;
        return new CentralDbContext(options);
    }
}

public class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ironmonkey_design;Username=postgres;Password=postgres")
            .Options;
        return new TenantDbContext(options, Guid.NewGuid());
    }
}
