using Npgsql;

namespace IronMonkey.ApiService.Common.Auth;

/// <summary>
/// Rebuilds a tenant's connection string against the server the app is talking to *now*.
///
/// Provisioning snapshots the whole central connection string — host, port, password —
/// into the tenant row. Under Aspire the PostgreSQL container gets a fresh random host port
/// (and password) on every restart, so those stored strings go stale and every tenant-scoped
/// request fails with a connection error until the tenant is reprovisioned.
///
/// Only the database name is genuinely per-tenant; the server coordinates always match the
/// central database, because tenant databases are created on that same server. So keep the
/// stored Database and take everything else from live configuration.
/// </summary>
public interface ITenantConnectionStringResolver
{
    /// <summary>
    /// Returns <paramref name="storedConnectionString"/> with its server coordinates replaced
    /// by the current central ones. Falls back to the stored value unchanged if either string
    /// cannot be parsed, so a malformed entry surfaces its own error rather than this one's.
    /// </summary>
    string Resolve(string storedConnectionString);
}

public sealed class TenantConnectionStringResolver(IConfiguration configuration)
    : ITenantConnectionStringResolver
{
    public string Resolve(string storedConnectionString)
    {
        var centralConnectionString = configuration.GetConnectionString("CentralDb");

        if (string.IsNullOrWhiteSpace(centralConnectionString))
            return storedConnectionString;

        try
        {
            var stored = new NpgsqlConnectionStringBuilder(storedConnectionString);

            // Nothing to point at — leave it alone rather than inventing a database name.
            if (string.IsNullOrEmpty(stored.Database))
                return storedConnectionString;

            return new NpgsqlConnectionStringBuilder(centralConnectionString)
            {
                Database = stored.Database
            }.ToString();
        }
        catch (ArgumentException)
        {
            // Not a parseable connection string; hand it back untouched.
            return storedConnectionString;
        }
    }
}
