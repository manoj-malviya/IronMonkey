var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var centralDb = postgres.AddDatabase("CentralDb");

var apiService = builder.AddProject<Projects.IronMonkey_ApiService>("apiservice")
    .WithReference(centralDb)
    .WaitFor(centralDb)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
