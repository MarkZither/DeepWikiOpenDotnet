using DeepWiki.ApiService.Configuration;
using DeepWiki.Data.Postgres.DependencyInjection;
using DeepWiki.Data.SqlServer.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Polly;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

namespace DeepWiki.ApiService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Enable DI validation to catch misconfigurations early when building the provider (useful for debugging)
        builder.Host.UseDefaultServiceProvider(opts => { opts.ValidateScopes = true; opts.ValidateOnBuild = true; });

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

                // Read configuration values (with sane defaults)
                var rlCfg = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimit");
                var permitLimit = rlCfg.GetValue<int?>("PermitLimit") ?? 100;
                var windowSec = rlCfg.GetValue<int?>("WindowSeconds") ?? 60;
                var queueLimit = rlCfg.GetValue<int?>("QueueLimit") ?? 10;
                var retryAfter = rlCfg.GetValue<int?>("RetryAfterSeconds") ?? 60;

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSec),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = queueLimit
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                var rlCfg = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimit");
                var retryAfter = rlCfg.GetValue<int?>("RetryAfterSeconds") ?? 60;
                var permitLimit = rlCfg.GetValue<int?>("PermitLimit") ?? 100;
                var windowSec = rlCfg.GetValue<int?>("WindowSeconds") ?? 60;

                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                context.HttpContext.Response.Headers["X-RateLimit-Limit"] = permitLimit.ToString();
                context.HttpContext.Response.Headers["X-RateLimit-Remaining"] = "0";
                context.HttpContext.Response.Headers["X-RateLimit-Reset"] = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + windowSec).ToString();

                await context.HttpContext.Response.WriteAsync(
                    $"Rate limit exceeded. Please retry after {retryAfter} seconds.", token);
            };
        });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        // --- Vector store & RAG services ---
        // Register VectorStore configuration options
        builder.Services.Configure<VectorStoreOptions>(
            builder.Configuration.GetSection(VectorStoreOptions.SectionName));

        // Register data layer services based on VectorStore:Provider configuration
        var vectorStoreProvider = builder.Configuration.GetValue<string>("VectorStore:Provider") ?? "postgres";
        var dataLayerRegistered = false;
        
        if (vectorStoreProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Register PostgreSQL data layer (DbContext, IDocumentRepository, IPersistenceVectorStore)
            var connectionString = builder.Configuration.GetConnectionString("deepwikidb");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                builder.Services.AddPostgresDataLayer(connectionString);
                dataLayerRegistered = true;
                
                // Add health check for PostgreSQL
                builder.Services.AddHealthChecks()
                    .AddDbContextCheck<DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext>("PostgresVectorDbContext");
            }
        }
        else if (vectorStoreProvider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) || 
                 vectorStoreProvider.Equals("mssql", StringComparison.OrdinalIgnoreCase))
        {
            // Register SQL Server data layer (DbContext, IDocumentRepository, IPersistenceVectorStore)
            var connectionString = builder.Configuration.GetConnectionString("SqlServer") 
                ?? builder.Configuration.GetValue<string>("VectorStore:SqlServer:ConnectionString");
            
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                builder.Services.AddSqlServerDataLayer(connectionString);
                dataLayerRegistered = true;
                
                // Add health check for SQL Server
                builder.Services.AddHealthChecks()
                    .AddDbContextCheck<DeepWiki.Data.SqlServer.DbContexts.SqlServerVectorDbContext>("SqlServerVectorDbContext");
            }
        }
        
        if (!dataLayerRegistered)
        {
            // No data layer registered - add minimal health checks
            // IDocumentRepository will need to be provided by test fixtures or will fail at DI resolution time
            builder.Services.AddHealthChecks();
        }

        // Register vector store via factory pattern
        // Configuration: Set "VectorStore:Provider" to "sqlserver" or "postgres" in appsettings.json
        // For SQL Server: Set "VectorStore:SqlServer:ConnectionString" or use ConnectionStrings:SqlServer
        // For Postgres: Set "VectorStore:Postgres:ConnectionString" or use ConnectionStrings:Postgres
        builder.Services.AddSingleton<DeepWiki.Rag.Core.VectorStore.VectorStoreFactory>();
        builder.Services.AddScoped<DeepWiki.Data.Abstractions.IVectorStore>(sp =>
        {
            // Lightweight logging for DI-time diagnosis
            var regLogger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Startup.VectorStoreRegistration");

            var factory = sp.GetRequiredService<DeepWiki.Rag.Core.VectorStore.VectorStoreFactory>();
            var provider = builder.Configuration.GetValue<string>("VectorStore:Provider") ?? "postgres";

            regLogger?.LogInformation("Resolving IVectorStore for provider '{Provider}'. ProviderAvailable={ProviderAvailable}", provider, factory.IsProviderAvailable(provider));
            
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

            regLogger?.LogInformation("Creating IVectorStore implementation for provider '{Provider}'", provider);
            return factory.Create(sp);
        });

        // Register tokenization service
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Tokenization.TokenEncoderFactory>();
        builder.Services.AddSingleton<DeepWiki.Data.Abstractions.ITokenizationService, DeepWiki.Rag.Core.Tokenization.TokenizationService>();

        // Session manager and generation service
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Services.SessionManager>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Observability.GenerationMetrics>();
        builder.Services.AddScoped<DeepWiki.Data.Abstractions.IGenerationService, DeepWiki.Rag.Core.Services.GenerationService>();

        // Register Ollama provider as a typed HttpClient and map the interface to it.
        // Using the concrete typed client ensures the concrete type can be resolved directly
        // (some consumers or DI validation may request the concrete type).
        builder.Services.AddHttpClient<DeepWiki.Rag.Core.Providers.OllamaProvider>((sp, client) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = cfg.GetValue<string>("Ollama:Endpoint") ?? "http://localhost:11434";
            client.BaseAddress = new Uri(endpoint);
        });
        builder.Services.AddScoped<DeepWiki.Rag.Core.Providers.IModelProvider>(sp =>
            sp.GetRequiredService<DeepWiki.Rag.Core.Providers.OllamaProvider>());

        // Register embedding service via factory pattern
        // Configuration: Set "Embedding:Provider" to "openai", "foundry", or "ollama" in appsettings.json
        // For OpenAI: Set "Embedding:OpenAI:ApiKey" or OPENAI_API_KEY env var
        // For Foundry: Set "Embedding:Foundry:Endpoint" and optionally "Embedding:Foundry:ApiKey"
        // For Ollama: Set "Embedding:Ollama:Endpoint" (defaults to http://localhost:11434)
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Embedding.IEmbeddingCache, DeepWiki.Rag.Core.Embedding.EmbeddingCache>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Embedding.EmbeddingServiceFactory>();
        builder.Services.AddSingleton<DeepWiki.Data.Abstractions.IEmbeddingService>(sp =>
        {
            var regLogger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Startup.EmbeddingRegistration");

            var factory = sp.GetRequiredService<DeepWiki.Rag.Core.Embedding.EmbeddingServiceFactory>();
            var provider = builder.Configuration.GetValue<string>("Embedding:Provider");
            regLogger?.LogInformation("Resolving IEmbeddingService for provider '{Provider}'. ProviderAvailable={ProviderAvailable}", provider ?? "(not set)", factory.IsProviderAvailable(provider ?? string.Empty));
            
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
            
            regLogger?.LogInformation("Creating IEmbeddingService implementation for provider '{Provider}'", provider);
            return factory.Create();
        });

        // Register resilience pipeline for embedding service calls as a singleton
        // This avoids rebuilding the pipeline for every request, which is more efficient
        builder.Services.AddSingleton<ResiliencePipeline>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("EmbeddingResiliencePipeline");

            return new ResiliencePipelineBuilder()
                .AddRetry(new Polly.Retry.RetryStrategyOptions
                {
                    ShouldHandle = new Polly.PredicateBuilder().Handle<Exception>(),
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = Polly.DelayBackoffType.Exponential,
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Embedding service retry attempt {AttemptNumber} after {Delay}ms. Exception: {Exception}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
                {
                    ShouldHandle = new Polly.PredicateBuilder().Handle<Exception>(),
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(30),
                    OnOpened = args =>
                    {
                        logger.LogError(
                            "Circuit breaker opened for embedding service. Will retry after {BreakDuration}s",
                            args.BreakDuration.TotalSeconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
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
    var autoMigrate = config.GetValue<bool?>("VectorStore:AutoMigrate") ?? true;

    if (autoMigrate)
    {
        try
        {
            if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
            {
                // Strict requirement: ConnectionStrings:deepwikidb MUST be configured for Postgres
                var pgConn = config.GetConnectionString("deepwikidb");
                if (string.IsNullOrEmpty(pgConn))
                {
                    logger?.LogError("VectorStore:AutoMigrate is enabled but 'ConnectionStrings:deepwikidb' is not configured. Set this via user-secrets (for local dev) or configure Aspire to inject it. Startup will not continue.");
                    throw new InvalidOperationException("AutoMigrate requires 'ConnectionStrings:deepwikidb' to be configured. Configure ConnectionStrings:deepwikidb in appsettings or user-secrets.");
                }

                logger?.LogInformation("VectorStore:AutoMigrate enabled. Applying Postgres migrations...");
                var db = scope.ServiceProvider.GetRequiredService<DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext>();
                db.Database.Migrate();
                logger?.LogInformation("Postgres migrations applied successfully.");
            }
            else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) || provider.Equals("mssql", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("VectorStore:AutoMigrate enabled. Applying SQL Server migrations...");
                var db = scope.ServiceProvider.GetRequiredService<DeepWiki.Data.SqlServer.DbContexts.SqlServerVectorDbContext>();
                db.Database.Migrate();
                logger?.LogInformation("SQL Server migrations applied successfully.");
            }
            else
            {
                logger?.LogInformation("VectorStore:AutoMigrate enabled but provider '{Provider}' is not managed by AutoMigrate.", provider);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply vector store migrations during startup.");
            // Fail fast so startup does not continue in a misconfigured state
            throw;
        }
    }
}

        // Configure the HTTP request pipeline.
        // Development: show developer exception page so errors are visible in responses / logs
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        else
        {
            app.UseExceptionHandler();
        }

        // Early request logging middleware to help diagnose requests that never reach controllers
        app.Use(async (context, next) =>
        {
            app.Logger.LogInformation("Incoming request {Method} {Path} from {RemoteIp}", context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            try
            {
                await next();
                app.Logger.LogInformation("Request {Method} {Path} completed with status {StatusCode}", context.Request.Method, context.Request.Path, context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Unhandled exception while processing request {Method} {Path}", context.Request.Method, context.Request.Path);
                throw;
            }
        });

        // SECURITY: Enable rate limiting middleware
        app.UseRateLimiter();

        // Add headers for rate limit visibility (X-RateLimit-*) on all responses. Values are best-effort
        app.Use(async (ctx, next) =>
        {
            var rlCfg = ctx.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimit");
            var permitLimit = rlCfg.GetValue<int?>("PermitLimit") ?? 100;
            var windowSec = rlCfg.GetValue<int?>("WindowSeconds") ?? 60;

            ctx.Response.OnStarting(() =>
            {
                // Best-effort values; Remaining is unknown at this stage, so we leave it to the limiter on rejection
                if (!ctx.Response.Headers.ContainsKey("X-RateLimit-Limit"))
                    ctx.Response.Headers["X-RateLimit-Limit"] = permitLimit.ToString();
                if (!ctx.Response.Headers.ContainsKey("X-RateLimit-Remaining"))
                    ctx.Response.Headers["X-RateLimit-Remaining"] = "unknown";
                if (!ctx.Response.Headers.ContainsKey("X-RateLimit-Reset"))
                    ctx.Response.Headers["X-RateLimit-Reset"] = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + windowSec).ToString();
                return Task.CompletedTask;
            });

            await next();
        });

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
