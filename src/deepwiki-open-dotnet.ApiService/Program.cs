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
        // Stamp every console log line with HH:mm:ss so timing is visible in Aspire and terminal.
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "HH:mm:ss ";
            options.SingleLine      = true;
        });

        builder.AddServiceDefaults();

        // Add services to the container.
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers();

        // Task T069: Add SignalR for bidirectional streaming communication
        builder.Services.AddSignalR();

        // Task T070: Configure CORS for SignalR hub (local development and internal origins)
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("SignalRPolicy", policy =>
            {
                var cfg = builder.Configuration;
                var allowedOrigins = cfg.GetSection("SignalR:AllowedOrigins").Get<string[]>() 
                    ?? new[] { "http://localhost:3000", "http://localhost:5173", "http://localhost:8080" };
                
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials(); // Required for SignalR
            });
        });

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

        // Register Embedding configuration options
        builder.Services.Configure<EmbeddingOptions>(
            builder.Configuration.GetSection(EmbeddingOptions.SectionName));

        // Register data layer services based on VectorStore:Provider configuration
        var vectorStoreProvider = builder.Configuration.GetValue<string>("VectorStore:Provider");
        if (string.IsNullOrWhiteSpace(vectorStoreProvider))
        {
            throw new InvalidOperationException(
                "VectorStore:Provider is not configured. Set 'VectorStore:Provider' to 'postgres' or 'sqlserver' in appsettings.json or environment variables. See VECTOR_STORE_SETUP.md for configuration examples.");
        }
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
            // Fail fast: if a provider is configured but the connection string is missing the application
            // cannot function — crashing at startup is far better than silently losing all data.
            var connHint = vectorStoreProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
                ? "'ConnectionStrings:deepwikidb'"
                : "'ConnectionStrings:SqlServer' or 'VectorStore:SqlServer:ConnectionString'";
            throw new InvalidOperationException(
                $"VectorStore:Provider is '{vectorStoreProvider}' but its connection string is not configured. " +
                $"Set {connHint} in appsettings, user-secrets, or environment variables. " +
                $"See VECTOR_STORE_SETUP.md for configuration examples.");
        }
        // IVectorStore is registered directly by AddPostgresDataLayer / AddSqlServerDataLayer above.
        // No factory indirection needed — the data layer extensions wire it up correctly.

        // Register tokenization service
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Tokenization.TokenEncoderFactory>();
        builder.Services.AddSingleton<DeepWiki.Data.Abstractions.ITokenizationService, DeepWiki.Rag.Core.Tokenization.TokenizationService>();

        // Chunking options (T091)
        builder.Services.Configure<DeepWiki.Rag.Core.Ingestion.ChunkOptions>(
            builder.Configuration.GetSection("Embedding:Chunking"));

        // Session manager and generation service
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Services.SessionManager>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Observability.GenerationMetrics>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Observability.WikiMetrics>();

        // Wiki snapshot recording (T064a) — disabled by default; enable via Wiki:Snapshots:Enabled
        builder.Services.Configure<DeepWiki.Rag.Core.Snapshots.WikiSnapshotOptions>(
            builder.Configuration.GetSection("Wiki:Snapshots"));
        var snapshotsEnabled = builder.Configuration.GetValue<bool>("Wiki:Snapshots:Enabled");
        if (snapshotsEnabled)
            builder.Services.AddSingleton<DeepWiki.Rag.Core.Snapshots.IWikiSnapshotRecorder,
                DeepWiki.Rag.Core.Snapshots.WikiSnapshotRecorder>();
        else
            builder.Services.AddSingleton<DeepWiki.Rag.Core.Snapshots.IWikiSnapshotRecorder,
                DeepWiki.Rag.Core.Snapshots.NullWikiSnapshotRecorder>();
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Services.PromptCancellationRegistry>();
        builder.Services.AddScoped<DeepWiki.Data.Abstractions.IGenerationService>((sp) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var providers = sp.GetServices<DeepWiki.Rag.Core.Providers.IModelProvider>();
            // Respect configured provider ordering (Generation:Providers) if present; otherwise use registration order
            var orderedProviders = DeepWiki.Rag.Core.Providers.ProviderOrderResolver.ResolveOrder(providers, cfg);
            var sessionManager = sp.GetRequiredService<DeepWiki.Rag.Core.Services.SessionManager>();
            var metrics = sp.GetRequiredService<DeepWiki.Rag.Core.Observability.GenerationMetrics>();
            var logger = sp.GetRequiredService<ILogger<DeepWiki.Rag.Core.Services.GenerationService>>();
            var vectorStore = sp.GetService<DeepWiki.Data.Abstractions.IVectorStore>();
            var embeddingService = sp.GetService<DeepWiki.Data.Abstractions.IEmbeddingService>();
            var stallTimeoutSeconds = cfg.GetValue<int?>("Generation:Ollama:StallTimeoutSeconds") ?? 300;
            var failureThreshold = cfg.GetValue<int?>("Generation:ProviderFailureThreshold") ?? 3;
            var breakDurationSec = cfg.GetValue<int?>("Generation:ProviderCircuitBreakSeconds") ?? 30;
            var registry = sp.GetRequiredService<DeepWiki.Rag.Core.Services.PromptCancellationRegistry>();
            return new DeepWiki.Rag.Core.Services.GenerationService(
                orderedProviders, sessionManager, metrics, logger, vectorStore, embeddingService, 
                TimeSpan.FromSeconds(stallTimeoutSeconds), failureThreshold, TimeSpan.FromSeconds(breakDurationSec), registry);
        });

        // Register Ollama provider as a typed HttpClient and map the interface to it.
        // Only register if configured in Generation:Providers list
        var configuredProviders = builder.Configuration.GetSection("Generation:Providers").Get<string[]>() ?? Array.Empty<string>();
        if (configuredProviders.Any(p => p.Equals("Ollama", StringComparison.OrdinalIgnoreCase)))
        {
            // RemoveAllResilienceHandlers is marked [Experimental] (EXTEXP0001) but is the
            // documented pattern for replacing the global Aspire pipeline with a custom one.
#pragma warning disable EXTEXP0001
            builder.Services.AddHttpClient<DeepWiki.Rag.Core.Providers.OllamaProvider>((sp, client) =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var endpoint = cfg.GetValue<string>("Embedding:Ollama:Endpoint") ?? "http://localhost:11434";
                client.BaseAddress = new Uri(endpoint);
                // Disable the HttpClient hard deadline — the OllamaProvider manages its own
                // per-token stall timeout via CancellationTokenSource.CancelAfter, which
                // resets on every received chunk.  A fixed HttpClient timeout would race
                // against that mechanism and kill long (but healthy) reasoning-model responses.
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            // Remove the global 30s Aspire pipeline added by ConfigureHttpClientDefaults
            // in ServiceDefaults before adding our own. Without this the two pipelines
            // stack: the global one fires first and kills Ollama requests at 30 seconds.
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                // Wiki generation can produce large pages that take > 2 min on local models.
                // Match the OllamaProvider stall timeout (default 5 min) so Polly never
                // fires before the stall-detection mechanism has a chance to act.
                var localModelTimeout = TimeSpan.FromMinutes(8);
                options.AttemptTimeout.Timeout      = localModelTimeout;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(30);
                // SamplingDuration must be >= 2× AttemptTimeout per Polly validation
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(20);
            });
#pragma warning restore EXTEXP0001
            builder.Services.AddScoped<DeepWiki.Rag.Core.Providers.IModelProvider>(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var model = cfg.GetValue<string>("Generation:Ollama:ModelId") 
                    ?? cfg.GetValue<string>("Ollama:GenerationModel") 
                    ?? "gemma3";
                var stallTimeoutSeconds = cfg.GetValue<int?>("Generation:Ollama:StallTimeoutSeconds") ?? 300; // 5 minutes default
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DeepWiki.Rag.Core.Providers.OllamaProvider));
                var logger = sp.GetRequiredService<ILogger<DeepWiki.Rag.Core.Providers.OllamaProvider>>();
                return new DeepWiki.Rag.Core.Providers.OllamaProvider(httpClient, logger, model, TimeSpan.FromSeconds(stallTimeoutSeconds));
            });
        }

        // Register OpenAI provider (HTTP client based) so it can be pointed at OpenAI-compatible endpoints
        // Only register if configured in Generation:Providers list
        if (configuredProviders.Any(p => p.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)))
        {
            // RemoveAllResilienceHandlers is marked [Experimental] (EXTEXP0001) — see OllamaProvider above.
#pragma warning disable EXTEXP0001
            builder.Services.AddHttpClient("OpenAIProvider", (sp, client) =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var baseUrl = cfg.GetValue<string>("OpenAI:BaseUrl");
                if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl);
                // Allow long-running requests for local models
                client.Timeout = TimeSpan.FromMinutes(10);
            })
            // Remove the global 30s Aspire pipeline (same reason as OllamaProvider above)
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                var localModelTimeout = TimeSpan.FromMinutes(8);
                options.AttemptTimeout.Timeout      = localModelTimeout;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(20);
            });
#pragma warning restore EXTEXP0001

            builder.Services.AddScoped<DeepWiki.Rag.Core.Providers.IModelProvider>(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var apiKey = cfg.GetValue<string>("OpenAI:ApiKey") ?? cfg.GetValue<string>("OpenAI__ApiKey");
                var providerType = cfg.GetValue<string>("OpenAI:Provider") ?? "openai";
                var modelId = cfg.GetValue<string>("OpenAI:ModelId") ?? "phi4-mini";
                var baseUrl = cfg.GetValue<string>("OpenAI:BaseUrl") ?? "(not set)";
                var logger = sp.GetRequiredService<ILogger<DeepWiki.Rag.Core.Providers.OpenAIProvider>>();

                // Startup diagnostic — confirm what the provider resolved to
                var keyStatus = string.IsNullOrEmpty(apiKey)
                    ? "NOT SET"
                    : $"set ({apiKey.Length} chars, ends ...{apiKey[^Math.Min(4, apiKey.Length)..]})";
                logger.LogInformation(
                    "OpenAI provider resolved — BaseUrl={BaseUrl}, Provider={Provider}, Model={Model}, ApiKey={KeyStatus}",
                    baseUrl, providerType, modelId, keyStatus);

                var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var client = clientFactory.CreateClient("OpenAIProvider");
                return new DeepWiki.Rag.Core.Providers.OpenAIProvider(client, apiKey, providerType, modelId, logger);
            });
        }

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
            
            // Fail fast — there is no useful fallback when embedding is not configured.
            if (string.IsNullOrEmpty(provider))
            {
                throw new InvalidOperationException(
                    "Embedding:Provider is not configured. Set it to 'ollama', 'openai', or 'foundry' " +
                    "in appsettings or environment variables.");
            }
            if (!factory.IsProviderAvailable(provider))
            {
                var hint = provider.ToLowerInvariant() switch
                {
                    "openai"             => "Set Embedding:OpenAI:ApiKey.",
                    "foundry" or "azure" => "Set Embedding:Foundry:Endpoint.",
                    "ollama"             => "Set Embedding:Ollama:Endpoint.",
                    _                    => string.Empty
                };
                throw new InvalidOperationException(
                    ($"Embedding provider '{provider}' is not available — required configuration is missing. {hint}").TrimEnd());
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

        // Wiki services (Phase 3: US1 MVP — CRUD operations)
        builder.Services.AddScoped<DeepWiki.Rag.Core.Services.IWikiService, DeepWiki.Rag.Core.Services.WikiService>();
        // Wiki export service (Phase 6: US4 — Export Wiki as Markdown or JSON)
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Services.IWikiExportService, DeepWiki.Rag.Core.Services.WikiExportService>();
        // Wiki generation service (Phase 7: US5 — Generate Wiki from Collection)
        builder.Services.AddScoped<DeepWiki.Rag.Core.Services.IWikiGenerationService, DeepWiki.Rag.Core.Services.WikiGenerationOrchestrator>();
        builder.Services.Configure<DeepWiki.Rag.Core.Models.WikiGenerationOptions>(
            builder.Configuration.GetSection("Wiki:Generation"));
        // SignalR notifier for wiki generation progress (singleton — IHubContext is thread-safe)
        builder.Services.AddSingleton<DeepWiki.Rag.Core.Services.IWikiProgressNotifier,
            DeepWiki.ApiService.Services.SignalRWikiProgressNotifier>();

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
        // Non-production: show developer exception page and expose OpenAPI / Scalar docs
        if (!app.Environment.IsProduction())
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
            // Skip health / metrics / static-asset paths — they are called frequently and add noise
            var path = context.Request.Path.Value ?? string.Empty;
            var isNoisy = path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/alive", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/_", StringComparison.OrdinalIgnoreCase);

            if (!isNoisy)
                app.Logger.LogInformation("Incoming {Method} {Path} from {RemoteIp}",
                    context.Request.Method, context.Request.Path,
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            try
            {
                await next();
                if (!isNoisy)
                    app.Logger.LogInformation("{Method} {Path} → {StatusCode}",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Unhandled exception: {Method} {Path}",
                    context.Request.Method, context.Request.Path);
                throw;
            }
        });

        // Task T070: Enable CORS for SignalR connections
        app.UseCors("SignalRPolicy");

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

        // Task T069: Map SignalR hub for streaming generation
        app.MapHub<DeepWiki.ApiService.Hubs.GenerationHub>("/hubs/generation");
        // Wiki progress hub — clients subscribe by wiki ID for real-time generation progress
        app.MapHub<DeepWiki.ApiService.Hubs.WikiProgressHub>("/hubs/wiki-progress");

        // Optionally expose Prometheus-compatible /metrics endpoint for scraping when enabled in configuration
        var promEnabled = app.Configuration.GetValue<bool?>("OpenTelemetry:Prometheus:Enabled") ?? false;
        if (promEnabled)
        {
            app.MapGet("/metrics", (IServiceProvider sp) =>
            {
                var gm = sp.GetService<DeepWiki.Rag.Core.Observability.GenerationMetrics>();
                var txt = gm?.ExportPrometheusMetrics() ?? "# no metrics exported\n";
                return Results.Text(txt, "text/plain");
            });
        }

        app.MapDefaultEndpoints();

        // Graceful shutdown: cancel in-flight prompts when application is stopping
        var lifetime = app.Lifetime;
        lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                // Use the global prompt registry to cancel any in-flight prompt streams.
                var registry = app.Services.GetService<DeepWiki.Rag.Core.Services.PromptCancellationRegistry>();
                if (registry != null)
                {
                    var activeCount = registry.ActivePromptIds().Count;
                    if (activeCount > 0)
                    {
                        app.Logger.LogInformation("Cancelling {Count} in-flight prompt(s) during shutdown", activeCount);
                    }
                    registry.CancelAll();
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error while cancelling in-flight prompts during shutdown");
            }
        });

        app.Run();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
