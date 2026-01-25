using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

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

// --- Vector store & RAG services ---
// Register vector store (placeholder for now, will be wired to actual provider in Slice 4)
builder.Services.AddSingleton<DeepWiki.Data.Abstractions.IVectorStore, DeepWiki.Rag.Core.VectorStore.NoOpVectorStore>();

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
builder.Services.AddSingleton<DeepWiki.Data.Abstractions.IDocumentIngestionService, DeepWiki.Rag.Core.Ingestion.DocumentIngestionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// SECURITY: Enable rate limiting middleware
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
