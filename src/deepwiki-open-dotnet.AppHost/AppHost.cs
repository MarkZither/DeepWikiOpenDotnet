var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration["Aspire:Dashboard:Port"] = "18888";

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.deepwiki_open_dotnet_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.deepwiki_open_dotnet_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
