using DeepWiki.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Implementation of ITokenizationService with factory injection for provider-specific encoders.
/// Supports OpenAI, Microsoft AI Foundry, and Ollama token counting with chunking capabilities.
/// </summary>
public sealed class TokenizationService : ITokenizationService
{
    private readonly TokenEncoderFactory _encoderFactory;
    private readonly ILogger<TokenizationService> _logger;

    // Cache encoders for frequently used models to avoid recreation
    private readonly Dictionary<string, ITokenEncoder> _encoderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    /// <summary>
    /// Creates a new tokenization service.
    /// </summary>
    /// <param name="encoderFactory">Factory for creating provider-specific token encoders.</param>
    /// <param name="logger">Optional logger.</param>
    public TokenizationService(TokenEncoderFactory encoderFactory, ILogger<TokenizationService> logger)
    {
        _encoderFactory = encoderFactory ?? throw new ArgumentNullException(nameof(encoderFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Construction log to trace when the tokenization service is resolved
        _logger.LogInformation("TokenizationService constructed. EncoderFactoryType={EncoderFactoryType}", encoderFactory?.GetType().Name);
    }

    /// <inheritdoc />
    public Task<int> CountTokensAsync(string text, string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(text))
            return Task.FromResult(0);

        var encoder = GetOrCreateEncoder(modelId);
        var count = encoder.CountTokens(text);

        _logger.LogDebug("Counted {TokenCount} tokens for text of length {Length} using model {Model}",
            count, text.Length, modelId);

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextChunk>> ChunkAsync(
        string text,
        int maxTokens = 8192,
        string? modelId = null,
        Guid? parentId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(text))
            return Task.FromResult<IReadOnlyList<TextChunk>>([]);

        var effectiveModelId = modelId ?? "text-embedding-ada-002";
        var encoder = GetOrCreateEncoder(effectiveModelId);

        // Detect language for metadata
        var language = Chunker.DetectLanguage(text);

        // Create chunker with the appropriate encoder
        // Create chunker with the service logger
        var chunker = new Chunker(encoder, Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<Chunker>(
                new LoggerFactory([new LoggerProvider(_logger)])));

        var chunks = chunker.ChunkText(text, maxTokens, parentId, language);

        _logger.LogDebug("Chunked text of length {Length} into {ChunkCount} chunks using model {Model}",
            text.Length, chunks.Count, effectiveModelId);

        // Validate word boundaries
        foreach (var chunk in chunks)
        {
            if (!Chunker.ValidateWordBoundaries(chunk, text))
            {
                _logger.LogWarning("Chunk {ChunkIndex} has mid-word split boundaries", chunk.ChunkIndex);
            }
        }

        return Task.FromResult(chunks);
    }

    /// <inheritdoc />
    public int GetMaxTokens(string modelId)
    {
        return TokenizationConfig.GetMaxTokens(modelId);
    }

    private ITokenEncoder GetOrCreateEncoder(string modelId)
    {
        var key = string.IsNullOrEmpty(modelId) ? "_default_" : modelId;

        lock (_cacheLock)
        {
            if (_encoderCache.TryGetValue(key, out var cached))
                return cached;

            var encoder = string.IsNullOrEmpty(modelId)
                ? _encoderFactory.GetDefaultEncoder()
                : _encoderFactory.CreateEncoder(modelId);

            _encoderCache[key] = encoder;
            return encoder;
        }
    }

    /// <summary>
    /// Helper class to create a logger for the chunker.
    /// </summary>
    private sealed class LoggerProvider(ILogger parentLogger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => parentLogger;
        public void Dispose() { }
    }
}
