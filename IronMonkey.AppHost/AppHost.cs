var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var centralDb = postgres.AddDatabase("CentralDb");

var apiService = builder.AddProject<Projects.IronMonkey_ApiService>("apiservice")
    .WithReference(centralDb)
    .WaitFor(centralDb)
    .WithHttpHealthCheck("/health");

var webFrontend = builder.AddProject<Projects.IronMonkey_Web>("webfrontend")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
