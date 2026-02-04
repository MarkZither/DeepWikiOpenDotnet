var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration["Aspire:Dashboard:Port"] = "18888";

var cache = builder.AddRedis("cache");

// Add PostgreSQL with pgvector for vector store
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17")
    .WithDataVolume()
    .WithPgAdmin();

var deepwikiDb = postgres.AddDatabase("deepwikidb");

// Get Ollama endpoint from configuration (default to localhost)
var ollamaEndpoint = builder.Configuration["Embedding:Ollama:Endpoint"] ?? "http://localhost:11434";
var ollamaModel = builder.Configuration["Embedding:Ollama:ModelId"] ?? "nomic-embed-text";

var apiService = builder.AddProject<Projects.deepwiki_open_dotnet_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(deepwikiDb)
    .WaitFor(deepwikiDb)
    .WithEnvironment("VectorStore__Provider", "postgres")
    .WithEnvironment("Embedding__Provider", "ollama")
    .WithEnvironment("Embedding__Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("Embedding__Ollama__ModelId", ollamaModel);

builder.AddProject<Projects.deepwiki_open_dotnet_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
