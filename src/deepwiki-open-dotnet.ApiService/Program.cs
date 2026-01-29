using DeepWiki.ApiService.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

namespace DeepWiki.ApiService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add service defaults & Aspire client integrations.
        builder.AddServiceDefaults();

        // Add services to the container.
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers();

        // SECURITY: Add rate limiting to prevent API abuse
        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Rate limit by IP address (or authenticated user ID when auth is added)
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,           // 100 requests per window
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10              // Allow 10 requests to queue
                });
            });
            
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsync(
                    "Rate limit exceeded. Please retry after 60 seconds.", token);
            };
        });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        // --- Database context ---
        // Register PostgreSQL DbContext with pgvector support
        // Aspire provides the connection string via configuration, we configure pgvector at EF Core level
        builder.Services.AddDbContext<DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext>((sp, options) =>
        {
            var connectionString = builder.Configuration.GetConnectionString("deepwikidb");
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector());
            }
        });
        
        // Add health check for PostgreSQL
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext>("PostgresVectorDbContext");

        // --- Vector store & RAG services ---
        // Register VectorStore configuration options
        builder.Services.Configure<VectorStoreOptions>(
            builder.Configuration.GetSection(VectorStoreOptions.SectionName));

        // Register PostgreSQL services (repository and vector store)
        // Note: The connection string is already configured by Aspire via AddNpgsqlDbContext above
        // We only need to register the repositories and adapters
        builder.Services.AddScoped<DeepWiki.Data.Interfaces.IDocumentRepository, DeepWiki.Data.Postgres.Repositories.PostgresDocumentRepository>();
        builder.Services.AddScoped<DeepWiki.Data.Interfaces.IPersistenceVectorStore, DeepWiki.Data.Postgres.Repositories.PostgresVectorStore>();
        builder.Services.AddScoped<DeepWiki.Data.Postgres.VectorStore.PostgresVectorStoreAdapter>();

        // Register vector store via factory pattern
        // Configuration: Set "VectorStore:Provider" to "sqlserver" or "postgres" in appsettings.json
        // For SQL Server: Set "VectorStore:SqlServer:ConnectionString" or use ConnectionStrings:SqlServer
        // For Postgres: Set "VectorStore:Postgres:ConnectionString" or use ConnectionStrings:Postgres
        builder.Services.AddSingleton<DeepWiki.Rag.Core.VectorStore.VectorStoreFactory>();
        builder.Services.AddScoped<DeepWiki.Data.Abstractions.IVectorStore>(sp =>
        {
            var factory = sp.GetRequiredService<DeepWiki.Rag.Core.VectorStore.VectorStoreFactory>();
            var provider = builder.Configuration.GetValue<string>("VectorStore:Provider") ?? "postgres";
            
            // If configured provider is not available, either throw or optionally fall back to NoOp
            // New config: VectorStore:AllowNoOpFallback (bool). Default: false (throw) to fail fast when misconfigured.
            if (!factory.IsProviderAvailable(provider))
            {
                var allowFallback = builder.Configuration.GetValue<bool?>("VectorStore:AllowNoOpFallback") ?? false;
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<DeepWiki.Rag.Core.VectorStore.NoOpVectorStore>>();
                var msg = $"Vector store provider '{provider}' not configured or not available. ";

                if (!allowFallback)
                {
                    logger?.LogError(msg + "Failing fast because VectorStore:AllowNoOpFallback is false. Configure the provider or set VectorStore:AllowNoOpFallback=true to allow NoOp fallback.");
                    throw new InvalidOperationException(msg + "Configure VectorStore:Provider and required connection strings to enable vector storage, or set VectorStore:AllowNoOpFallback=true to permit NoOp fallback.");
                }

                logger?.LogWarning(msg + "Using NoOpVectorStore because VectorStore:AllowNoOpFallback=true.");
                return new DeepWiki.Rag.Core.VectorStore.NoOpVectorStore();
            }

            return factory.Create();
        });

        // Register tokenization service
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Tokenization.TokenEncoderFactory>();
        builder.Services.AddSingleton<DeepWiki.Data.Abstractions.ITokenizationService, DeepWiki.Rag.Core.Tokenization.TokenizationService>();

        // Register embedding service via factory pattern
        // Configuration: Set "Embedding:Provider" to "openai", "foundry", or "ollama" in appsettings.json
        // For OpenAI: Set "Embedding:OpenAI:ApiKey" or OPENAI_API_KEY env var
        // For Foundry: Set "Embedding:Foundry:Endpoint" and optionally "Embedding:Foundry:ApiKey"
        // For Ollama: Set "Embedding:Ollama:Endpoint" (defaults to http://localhost:11434)
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Embedding.IEmbeddingCache, DeepWiki.Rag.Core.Embedding.EmbeddingCache>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Embedding.EmbeddingServiceFactory>();
        builder.Services.AddSingleton<DeepWiki.Data.Abstractions.IEmbeddingService>(sp =>
        {
            var factory = sp.GetRequiredService<DeepWiki.Rag.Core.Embedding.EmbeddingServiceFactory>();
            var provider = builder.Configuration.GetValue<string>("Embedding:Provider");
            
            // If no provider is configured or configured provider is not available, use NoOp
            if (string.IsNullOrEmpty(provider) || !factory.IsProviderAvailable(provider))
            {
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<DeepWiki.Rag.Core.Embedding.NoOpEmbeddingService>>();
                logger?.LogWarning(
                    "Embedding provider '{Provider}' not configured or not available. Using NoOpEmbeddingService. " +
                    "Configure Embedding:Provider and required credentials to enable embeddings.",
                    provider ?? "(not set)");
                return new DeepWiki.Rag.Core.Embedding.NoOpEmbeddingService();
            }
            
            return factory.Create();
        });

        // Register document ingestion service (Slice 4: orchestrates chunking, embedding, upsert)
        builder.Services.AddScoped<DeepWiki.Data.Abstractions.IDocumentIngestionService, DeepWiki.Rag.Core.Ingestion.DocumentIngestionService>();

        var app = builder.Build();

// Optional: Auto-run EF Core migrations for Postgres vector DB when requested
// Configuration: VectorStore:AutoMigrate (bool). Default: false.
using (var scope = app.Services.CreateScope())
{
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
    var provider = config.GetValue<string>("VectorStore:Provider") ?? "postgres";
    var autoMigrate = config.GetValue<bool?>("VectorStore:AutoMigrate") ?? false;

    if (autoMigrate && provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            logger?.LogInformation("VectorStore:AutoMigrate enabled. Applying Postgres migrations...");
            var db = scope.ServiceProvider.GetRequiredService<DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext>();
            db.Database.Migrate();
            logger?.LogInformation("Postgres migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply Postgres migrations during startup.");
            // Fail fast so startup does not continue in a misconfigured state
            throw;
        }
    }
}

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler();

        // SECURITY: Enable rate limiting middleware
        app.UseRateLimiter();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

        app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

        app.MapGet("/weatherforecast", () =>
        {
            var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
                .ToArray();
            return forecast;
        })
        .WithName("GetWeatherForecast");

        // Map API controllers
        app.MapControllers();

        app.MapDefaultEndpoints();

        app.Run();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
