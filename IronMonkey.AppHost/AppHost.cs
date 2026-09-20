var builder = DistributedApplication.CreateBuilder(args);

// Tenant databases live on this server and are created at provisioning time, so the
// container must outlive a restart — without a volume, `make down` removes it and every
// provisioned tenant's database is destroyed along with it.
//
// The host port is pinned too. Provisioning snapshots the central connection string into
// each tenant row, so a port that changes on every restart strands all of them.
// ITenantConnectionStringResolver rebases those stored strings at read time as a safety
// net, but a stable port keeps the stored values correct in the first place.
// The password must be pinned as well. Aspire generates a fresh random one per run, which
// a persistent volume rejects — the data directory keeps the password it was initialized
// with, so every later run would fail to authenticate. Local dev only; override with
// `dotnet user-secrets set Parameters:postgres-password <value>`.
var postgresPassword = builder.AddParameter(
    "postgres-password",
    () => builder.Configuration["Parameters:postgres-password"] ?? "ironmonkey_local_dev",
    secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume("ironmonkey-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEndpoint("tcp", e => e.Port = 55432);

var centralDb = postgres.AddDatabase("CentralDb");

var apiService = builder.AddProject<Projects.IronMonkey_ApiService>("apiservice")
    .WithReference(centralDb)
    .WaitFor(centralDb)
    .WithHttpHealthCheck("/health");

// AddProject gives the child a curated environment rather than inheriting AppHost's,
// so the SuperAdmin seed credential has to be forwarded explicitly. Only set when
// present: PlatformAdminSeeder skips seeding on a blank password, and passing "" would
// look configured while still doing nothing.
foreach (var key in new[] { "PlatformAdmin__Email", "PlatformAdmin__Password" })
{
    if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value)
        apiService.WithEnvironment(key, value);
}

var webFrontend = builder.AddProject<Projects.IronMonkey_Web>("webfrontend")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
