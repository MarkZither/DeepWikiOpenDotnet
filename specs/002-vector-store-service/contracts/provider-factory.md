# Embedding Provider Factory

**Purpose**: Document the factory pattern for selecting and configuring embedding providers.

---

## Overview

The `EmbeddingServiceFactory` provides runtime selection of embedding providers based on configuration. This enables switching between OpenAI, Microsoft AI Foundry, and Ollama without code changes.

**Namespace**: `DeepWiki.Rag.Core.Embedding`

---

## Factory Pattern

### EmbeddingServiceFactory

```csharp
public sealed class EmbeddingServiceFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IEmbeddingCache? _cache;

    public const string ConfigurationSection = "Embedding";

    /// <summary>
    /// Creates an embedding service based on the configured provider.
    /// </summary>
    public IEmbeddingService Create();

    /// <summary>
    /// Creates an embedding service for a specific provider.
    /// </summary>
    public IEmbeddingService CreateForProvider(string provider);

    /// <summary>
    /// Gets the configured provider name.
    /// </summary>
    public string GetConfiguredProvider();

    /// <summary>
    /// Checks if a specific provider is available.
    /// </summary>
    public bool IsProviderAvailable(string provider);
}
```

---

## Provider Selection

### Configuration-Based Selection

The factory reads from `Embedding:Provider` in configuration:

```json
{
  "Embedding": {
    "Provider": "openai"  // or "foundry", "ollama"
  }
}
```

### Programmatic Selection

```csharp
var factory = new EmbeddingServiceFactory(configuration, loggerFactory);

// Use configured provider
var defaultService = factory.Create();

// Explicitly select provider
var openaiService = factory.CreateForProvider("openai");
var foundryService = factory.CreateForProvider("foundry");
var ollamaService = factory.CreateForProvider("ollama");
```

---

## Supported Providers

| Provider | Aliases | Default Model | Description |
|----------|---------|---------------|-------------|
| `openai` | - | text-embedding-ada-002 | OpenAI API (direct or Azure OpenAI) |
| `foundry` | `azure`, `azureopenai` | text-embedding-ada-002 | Microsoft AI Foundry / Foundry Local |
| `ollama` | - | nomic-embed-text | Local Ollama instance |

---

## Provider Configuration

### OpenAI

```json
{
  "Embedding": {
    "Provider": "openai",
    "OpenAI": {
      "ApiKey": "sk-...",
      "ModelId": "text-embedding-3-small",
      "Endpoint": null
    }
  }
}
```

**Environment Variables**:
- `OPENAI_API_KEY` - API key (fallback if not in config)

**Created Client**: `OpenAIEmbeddingClient`

### Microsoft AI Foundry

```json
{
  "Embedding": {
    "Provider": "foundry",
    "Foundry": {
      "Endpoint": "https://your-foundry.ai.azure.com",
      "ApiKey": "...",
      "DeploymentName": "text-embedding-ada-002",
      "ModelId": "text-embedding-ada-002"
    }
  }
}
```

**Environment Variables**:
- `AZURE_OPENAI_ENDPOINT` - Foundry endpoint (fallback)
- `AZURE_OPENAI_API_KEY` - API key (fallback)

**Created Client**: `FoundryEmbeddingClient`

### Ollama

```json
{
  "Embedding": {
    "Provider": "ollama",
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "ModelId": "nomic-embed-text"
    }
  }
}
```

**Environment Variables**:
- `OLLAMA_ENDPOINT` - Ollama server URL

**Created Client**: `OllamaEmbeddingClient`

---

## Dependency Injection

### Recommended Setup

```csharp
// Program.cs or Startup.cs
public static void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    // Register factory
    services.AddSingleton<EmbeddingServiceFactory>();
    
    // Register embedding cache (optional, for fallback)
    services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
    
    // Register IEmbeddingService using factory
    services.AddScoped<IEmbeddingService>(sp =>
    {
        var factory = sp.GetRequiredService<EmbeddingServiceFactory>();
        return factory.Create();
    });
}
```

### With NoOp Fallback (when not configured)

```csharp
services.AddScoped<IEmbeddingService>(sp =>
{
    var factory = sp.GetRequiredService<EmbeddingServiceFactory>();
    var provider = factory.GetConfiguredProvider();
    
    if (!factory.IsProviderAvailable(provider))
    {
        // Return no-op service when not configured
        return new NoOpEmbeddingService();
    }
    
    return factory.Create();
});
```

---

## Retry Policy Configuration

The factory creates a `RetryPolicy` with configurable parameters:

```json
{
  "Embedding": {
    "MaxRetries": 3,
    "BaseDelayMs": 100,
    "MaxDelayMs": 10000,
    "UseCacheFallback": true
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxRetries` | 3 | Maximum retry attempts |
| `BaseDelayMs` | 100 | Initial delay (exponential backoff) |
| `MaxDelayMs` | 10000 | Maximum delay cap |
| `UseCacheFallback` | true | Fall back to cached embedding on failure |

---

## Adding a New Provider

To add support for a new embedding provider:

### 1. Create Provider Client

```csharp
// src/DeepWiki.Rag.Core/Embedding/Providers/MyProviderEmbeddingClient.cs
public class MyProviderEmbeddingClient : BaseEmbeddingClient
{
    public override string Provider => "myprovider";
    public override string ModelId { get; }
    public override int EmbeddingDimension => 1536;

    public MyProviderEmbeddingClient(
        string apiKey,
        string modelId,
        RetryPolicy retryPolicy,
        IEmbeddingCache? cache = null,
        ILogger? logger = null)
        : base(retryPolicy, cache, logger)
    {
        ModelId = modelId;
        // Initialize client...
    }

    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        // Call provider API
        // Return 1536-dim vector
    }
}
```

### 2. Update Factory

```csharp
// In EmbeddingServiceFactory.CreateForProvider:
return provider.ToLowerInvariant() switch
{
    "openai" => CreateOpenAIClient(section, retryPolicy),
    "foundry" => CreateFoundryClient(section, retryPolicy),
    "ollama" => CreateOllamaClient(section, retryPolicy),
    "myprovider" => CreateMyProviderClient(section, retryPolicy),  // Add this
    _ => throw new ArgumentException(...)
};

private MyProviderEmbeddingClient CreateMyProviderClient(
    IConfigurationSection section,
    RetryPolicy retryPolicy)
{
    var mySection = section.GetSection("MyProvider");
    var apiKey = mySection["ApiKey"]
        ?? Environment.GetEnvironmentVariable("MY_PROVIDER_API_KEY")
        ?? throw new InvalidOperationException("MyProvider API key not configured");
    
    return new MyProviderEmbeddingClient(
        apiKey: apiKey,
        modelId: mySection["ModelId"] ?? "default-model",
        retryPolicy: retryPolicy,
        cache: _cache,
        logger: _loggerFactory?.CreateLogger<MyProviderEmbeddingClient>());
}
```

### 3. Update IsProviderAvailable

```csharp
public bool IsProviderAvailable(string provider)
{
    var section = _configuration.GetSection(ConfigurationSection);
    return provider.ToLowerInvariant() switch
    {
        "openai" => !string.IsNullOrEmpty(section["OpenAI:ApiKey"]),
        "foundry" => !string.IsNullOrEmpty(section["Foundry:Endpoint"]),
        "ollama" => !string.IsNullOrEmpty(section["Ollama:Endpoint"]),
        "myprovider" => !string.IsNullOrEmpty(section["MyProvider:ApiKey"]),  // Add this
        _ => false
    };
}
```

### 4. Add Configuration Documentation

Update `quickstart.md` with configuration examples for the new provider.

### 5. Add Tests

```csharp
[Fact]
public void Factory_CreatesMyProviderClient_WhenConfigured()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Embedding:Provider"] = "myprovider",
            ["Embedding:MyProvider:ApiKey"] = "test-key"
        })
        .Build();
    
    var factory = new EmbeddingServiceFactory(config);
    var service = factory.Create();
    
    Assert.Equal("myprovider", service.Provider);
}
```

---

## Error Handling

### Unknown Provider

```csharp
try
{
    var service = factory.CreateForProvider("unknown");
}
catch (ArgumentException ex)
{
    // "Unknown embedding provider: 'unknown'. Supported providers: openai, foundry, ollama."
}
```

### Missing Configuration

```csharp
try
{
    var service = factory.CreateForProvider("openai");
}
catch (InvalidOperationException ex)
{
    // "OpenAI API key not configured. Set Embedding:OpenAI:ApiKey or OPENAI_API_KEY environment variable."
}
```

### Check Availability First

```csharp
if (factory.IsProviderAvailable("openai"))
{
    var service = factory.CreateForProvider("openai");
}
else
{
    logger.LogWarning("OpenAI provider not configured");
}
```

---

## See Also

- [IEmbeddingService](./IEmbeddingService.md) - Embedding service interface
- [quickstart.md](../quickstart.md) - Configuration examples for all providers
