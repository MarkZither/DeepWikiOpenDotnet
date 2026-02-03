using System.Diagnostics;
using System.Text.Json;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Ingestion;

/// <summary>
/// Document ingestion service that orchestrates chunking, embedding, and upsert operations.
/// Supports batch ingestion with duplicate detection and concurrent write handling.
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IVectorStore _vectorStore;
    private readonly ITokenizationService _tokenizationService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<DocumentIngestionService> _logger;

    // === SECURITY CONSTANTS ===
    // Maximum document text size in bytes (5 MB) - prevents memory exhaustion attacks
    private const int MaxTextBytes = 5 * 1024 * 1024;
    
    // Maximum token count per document (500K) - prevents excessive embedding costs
    private const int MaxTokenCount = 500_000;
    
    // Suspicious patterns that may indicate prompt injection attempts
    // These are logged/flagged but not blocked to allow legitimate use cases
    private static readonly string[] SuspiciousPatterns = new[]
    {
        "ignore previous instructions",
        "ignore all previous",
        "disregard above",
        "forget everything",
        "system prompt",
        "you are now",
        "act as if",
        "pretend you are",
        "new instructions:",
        "[INST]",
        "<|im_start|>",
        "### Human:",
        "### Assistant:"
    };

    // File type mappings for metadata enrichment
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "fs", "vb", "js", "ts", "tsx", "jsx", "py", "rb", "go", "rs", "java", "kt", "swift",
        "c", "cpp", "h", "hpp", "m", "mm", "php", "pl", "sh", "bash", "ps1", "psm1"
    };

    private static readonly HashSet<string> TestPathPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "test", "tests", "spec", "specs", "__tests__", "__test__"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "yaml", "yml", "xml", "toml", "ini", "conf", "config", "env"
    };

    /// <summary>
    /// Creates a new document ingestion service.
    /// </summary>
    /// <param name="vectorStore">The vector store for document storage.</param>
    /// <param name="tokenizationService">The tokenization service for chunking.</param>
    /// <param name="embeddingService">The embedding service for generating vectors.</param>
    /// <param name="logger">Optional logger.</param>
    public DocumentIngestionService(IVectorStore vectorStore, ITokenizationService tokenizationService,
        IEmbeddingService embeddingService, ILogger<DocumentIngestionService> logger)
    {
        ArgumentNullException.ThrowIfNull(vectorStore);
        ArgumentNullException.ThrowIfNull(tokenizationService);
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(logger);

        _vectorStore = vectorStore;
        _tokenizationService = tokenizationService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Documents.Count == 0)
        {
            _logger.LogWarning("Ingestion request contains no documents");
            return IngestionResult.Empty;
        }

        var sw = Stopwatch.StartNew();
        var successCount = 0;
        var totalChunks = 0;
        var errors = new List<IngestionError>();
        var ingestedIds = new List<Guid>();

        _logger.LogInformation(
            "Starting ingestion of {DocumentCount} documents with batch size {BatchSize}",
            request.Documents.Count, request.BatchSize);

        foreach (var doc in request.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docIdentifier = $"{doc.RepoUrl}:{doc.FilePath}";

            try
            {
                // Validate document
                ValidateDocument(doc);

                // Convert to DocumentDto with metadata enrichment
                var documentDto = await CreateDocumentDtoAsync(doc, request, cancellationToken);

                // Upsert to vector store
                await UpsertAsync(documentDto, cancellationToken);

                // Count chunks (single document = 1 chunk for now, chunking handled by ChunkAndEmbedAsync)
                totalChunks++;
                successCount++;
                ingestedIds.Add(documentDto.Id);

                _logger.LogDebug("Successfully ingested document {DocId}: {DocIdentifier}",
                    documentDto.Id, docIdentifier);
            }
            catch (Exception ex) when (request.ContinueOnError && ex is not OperationCanceledException)
            {
                var stage = DetermineStage(ex);
                var error = IngestionError.FromException(docIdentifier, ex, stage);
                errors.Add(error);

                _logger.LogWarning(ex, "Failed to ingest document {DocIdentifier} at stage {Stage}: {Error}",
                    docIdentifier, stage, ex.Message);
            }
        }

        sw.Stop();

        var result = new IngestionResult
        {
            SuccessCount = successCount,
            FailureCount = errors.Count,
            TotalChunks = totalChunks,
            DurationMs = sw.ElapsedMilliseconds,
            IngestedDocumentIds = ingestedIds,
            Errors = errors
        };

        _logger.LogInformation(
            "Ingestion complete: {Success}/{Total} documents ({Rate:F1} docs/sec), {ChunkCount} chunks, {ErrorCount} errors in {Duration}ms",
            successCount, request.Documents.Count, result.DocumentsPerSecond, totalChunks, errors.Count, sw.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<DocumentDto> UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        ValidateDocumentDto(document);

        var sw = Stopwatch.StartNew();

        try
        {
            // Ensure we have an ID
            if (document.Id == Guid.Empty)
            {
                document.Id = Guid.NewGuid();
            }

            // Set timestamps
            var now = DateTime.UtcNow;
            if (document.CreatedAt == default)
            {
                document.CreatedAt = now;
            }
            document.UpdatedAt = now;

            // Delegate to vector store (which handles duplicate detection by RepoUrl+FilePath)
            await _vectorStore.UpsertAsync(document, cancellationToken);

            sw.Stop();
            _logger.LogDebug(
                "Upserted document {DocId} ({RepoUrl}:{FilePath}) in {Duration}ms",
                document.Id, document.RepoUrl, document.FilePath, sw.ElapsedMilliseconds);

            return document;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to upsert document {DocId} ({RepoUrl}:{FilePath}) after {Duration}ms: {Error}",
                document.Id, document.RepoUrl, document.FilePath, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkEmbeddingResult>> ChunkAndEmbedAsync(
        string text,
        int maxTokensPerChunk = 8192,
        Guid? parentDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var sw = Stopwatch.StartNew();

        // Get token count to validate
        var totalTokens = await _tokenizationService.CountTokensAsync(text, _embeddingService.ModelId, cancellationToken);

        _logger.LogDebug(
            "ChunkAndEmbed: text length {Length}, total tokens {Tokens}, max per chunk {Max}",
            text.Length, totalTokens, maxTokensPerChunk);

        // Chunk the text
        var chunks = await _tokenizationService.ChunkAsync(
            text,
            maxTokensPerChunk,
            _embeddingService.ModelId,
            parentDocumentId,
            cancellationToken);

        if (chunks.Count == 0)
        {
            return [];
        }

        // Validate all chunks are within token limit
        foreach (var chunk in chunks)
        {
            if (chunk.TokenCount > maxTokensPerChunk)
            {
                throw new InvalidOperationException(
                    $"Chunk {chunk.ChunkIndex} has {chunk.TokenCount} tokens, exceeding limit of {maxTokensPerChunk}");
            }
        }

        // Embed all chunks using batch embedding for efficiency
        var chunkTexts = chunks.Select(c => c.Text).ToList();
        var embeddings = new List<float[]>();

        await foreach (var embedding in _embeddingService.EmbedBatchAsync(chunkTexts, cancellationToken))
        {
            embeddings.Add(embedding);
        }

        if (embeddings.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count mismatch: got {embeddings.Count} embeddings for {chunks.Count} chunks");
        }

        // Build results
        var results = new List<ChunkEmbeddingResult>();
        for (var i = 0; i < chunks.Count; i++)
        {
            results.Add(new ChunkEmbeddingResult
            {
                Text = chunks[i].Text,
                Embedding = embeddings[i],
                ChunkIndex = chunks[i].ChunkIndex,
                ParentDocumentId = parentDocumentId,
                TokenCount = chunks[i].TokenCount,
                Language = chunks[i].Language,
                EmbeddingLatencyMs = sw.ElapsedMilliseconds / chunks.Count // Average per chunk
            });
        }

        sw.Stop();
        _logger.LogInformation(
            "ChunkAndEmbed complete: {ChunkCount} chunks, {TotalTokens} total tokens in {Duration}ms",
            results.Count, totalTokens, sw.ElapsedMilliseconds);

        return results;
    }

    /// <summary>
    /// Creates a DocumentDto from an IngestionDocument with metadata enrichment.
    /// </summary>
    private async Task<DocumentDto> CreateDocumentDtoAsync(
        IngestionDocument doc,
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        // SECURITY: Detect and flag potential prompt injection attempts
        var suspiciousContent = DetectSuspiciousContent(doc.Text);
        if (suspiciousContent is not null)
        {
            _logger.LogWarning(
                "Potential prompt injection detected in document {RepoUrl}:{FilePath} - pattern: {Pattern}",
                doc.RepoUrl, doc.FilePath, suspiciousContent);
        }

        // SECURITY: Sanitize metadata JSON to prevent injection via malformed JSON
        var sanitizedMetadata = SanitizeMetadataJson(doc.MetadataJson);

        var fileType = doc.FileType ?? GetFileType(doc.FilePath);
        var isCode = doc.IsCode ?? IsCodeFile(fileType);
        var isImplementation = doc.IsImplementation ?? IsImplementationFile(doc.FilePath, isCode);

        // Generate embedding if not provided and not skipped
        float[] embedding;
        if (doc.Embedding is not null && doc.Embedding.Length > 0)
        {
            embedding = doc.Embedding;
        }
        else if (request.SkipEmbedding)
        {
            embedding = Array.Empty<float>();
        }
        else
        {
            // For large documents, chunk and embed, then use first chunk's embedding
            // (full chunking support would create multiple DocumentDtos)
            var tokenCount = await _tokenizationService.CountTokensAsync(
                doc.Text, _embeddingService.ModelId, cancellationToken);

            if (tokenCount <= request.MaxTokensPerChunk)
            {
                embedding = await _embeddingService.EmbedAsync(doc.Text, cancellationToken);
            }
            else
            {
                // Chunk and embed, return first chunk's embedding for the main document
                var chunkResults = await ChunkAndEmbedAsync(
                    doc.Text, request.MaxTokensPerChunk, doc.Id, cancellationToken);

                embedding = chunkResults.Count > 0 ? chunkResults[0].Embedding : Array.Empty<float>();

                _logger.LogDebug(
                    "Document {FilePath} chunked into {ChunkCount} chunks due to size ({Tokens} tokens)",
                    doc.FilePath, chunkResults.Count, tokenCount);
            }
        }

        // Merge metadata (using sanitized version)
        var metadata = MergeMetadata(sanitizedMetadata, request.MetadataDefaults, new Dictionary<string, object>
        {
            ["file_type"] = fileType,
            ["is_code"] = isCode,
            ["is_implementation"] = isImplementation,
            ["language"] = isCode ? DetectCodeLanguage(fileType) : "text",
            ["_suspicious_content_detected"] = suspiciousContent is not null
        });

        var tokenCountValue = await _tokenizationService.CountTokensAsync(
            doc.Text, _embeddingService.ModelId, cancellationToken);

        return new DocumentDto
        {
            Id = doc.Id ?? Guid.NewGuid(),
            RepoUrl = doc.RepoUrl,
            FilePath = doc.FilePath,
            Title = string.IsNullOrEmpty(doc.Title) ? Path.GetFileName(doc.FilePath) : doc.Title,
            Text = doc.Text,
            Embedding = embedding,
            MetadataJson = metadata,
            TokenCount = tokenCountValue,
            FileType = fileType,
            IsCode = isCode,
            IsImplementation = isImplementation
        };
    }

    private static void ValidateDocument(IngestionDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.RepoUrl))
            throw new ArgumentException("Document RepoUrl is required", nameof(doc));

        if (string.IsNullOrWhiteSpace(doc.FilePath))
            throw new ArgumentException("Document FilePath is required", nameof(doc));

        if (string.IsNullOrWhiteSpace(doc.Text))
            throw new ArgumentException("Document Text is required", nameof(doc));

        // SECURITY: Enforce document size limits to prevent memory exhaustion
        var textBytes = System.Text.Encoding.UTF8.GetByteCount(doc.Text);
        if (textBytes > MaxTextBytes)
        {
            throw new ArgumentException(
                $"Document text exceeds maximum size of {MaxTextBytes / (1024 * 1024)} MB (got {textBytes / (1024 * 1024.0):F2} MB)",
                nameof(doc));
        }
    }

    private static void ValidateDocumentDto(DocumentDto doc)
    {
        if (string.IsNullOrWhiteSpace(doc.RepoUrl))
            throw new ArgumentException("Document RepoUrl is required", nameof(doc));

        if (string.IsNullOrWhiteSpace(doc.FilePath))
            throw new ArgumentException("Document FilePath is required", nameof(doc));

        if (string.IsNullOrWhiteSpace(doc.Text))
            throw new ArgumentException("Document Text is required", nameof(doc));

        // Validate embedding dimensionality if present
        if (doc.Embedding is not null && doc.Embedding.Length > 0 && doc.Embedding.Length != 1536)
        {
            throw new ArgumentException(
                $"Embedding must be 1536 dimensions, got {doc.Embedding.Length}", nameof(doc));
        }
    }

    private static string GetFileType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(extension) ? "unknown" : extension.TrimStart('.').ToLowerInvariant();
    }

    private static bool IsCodeFile(string fileType)
    {
        return CodeExtensions.Contains(fileType);
    }

    private static bool IsImplementationFile(string filePath, bool isCode)
    {
        if (!isCode) return false;

        // Check if path contains test-related patterns
        var pathParts = filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in pathParts)
        {
            if (TestPathPatterns.Contains(part))
                return false;
        }

        // Check filename for test patterns
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Spec", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string DetectCodeLanguage(string fileType)
    {
        return fileType.ToLowerInvariant() switch
        {
            "cs" => "csharp",
            "fs" => "fsharp",
            "vb" => "vb",
            "js" => "javascript",
            "ts" or "tsx" => "typescript",
            "jsx" => "javascript",
            "py" => "python",
            "rb" => "ruby",
            "go" => "go",
            "rs" => "rust",
            "java" => "java",
            "kt" => "kotlin",
            "swift" => "swift",
            "c" or "h" => "c",
            "cpp" or "hpp" or "cc" or "cxx" => "cpp",
            "m" or "mm" => "objective-c",
            "php" => "php",
            "pl" => "perl",
            "sh" or "bash" => "bash",
            "ps1" or "psm1" => "powershell",
            _ => "code"
        };
    }

    private static string MergeMetadata(
        string documentMetadataJson,
        Dictionary<string, string>? defaults,
        Dictionary<string, object> enrichments)
    {
        var metadata = new Dictionary<string, object>();

        // Start with defaults
        if (defaults is not null)
        {
            foreach (var (key, value) in defaults)
            {
                metadata[key] = value;
            }
        }

        // Add document metadata (overrides defaults)
        if (!string.IsNullOrWhiteSpace(documentMetadataJson) && documentMetadataJson != "{}")
        {
            try
            {
                var docMeta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(documentMetadataJson);
                if (docMeta is not null)
                {
                    foreach (var (key, value) in docMeta)
                    {
                        metadata[key] = value.ValueKind switch
                        {
                            JsonValueKind.String => value.GetString() ?? "",
                            JsonValueKind.Number => value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => value.ToString()
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, skip document metadata
            }
        }

        // Add enrichments (overrides both)
        foreach (var (key, value) in enrichments)
        {
            metadata[key] = value;
        }

        return JsonSerializer.Serialize(metadata);
    }

    private static IngestionStage DetermineStage(Exception ex)
    {
        return ex switch
        {
            ArgumentException => IngestionStage.Validation,
            InvalidOperationException e when e.Message.Contains("token", StringComparison.OrdinalIgnoreCase) => IngestionStage.Chunking,
            InvalidOperationException e when e.Message.Contains("embedding", StringComparison.OrdinalIgnoreCase) => IngestionStage.Embedding,
            _ => IngestionStage.Upsert
        };
    }

    /// <summary>
    /// SECURITY: Detects potential prompt injection patterns in document content.
    /// Returns the first matched pattern or null if no suspicious content is found.
    /// Note: This flags but does not block - legitimate code may contain these patterns.
    /// </summary>
    private static string? DetectSuspiciousContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var lowerText = text.ToLowerInvariant();
        foreach (var pattern in SuspiciousPatterns)
        {
            if (lowerText.Contains(pattern.ToLowerInvariant()))
            {
                return pattern;
            }
        }
        return null;
    }

    /// <summary>
    /// SECURITY: Sanitizes metadata JSON by parsing and re-serializing.
    /// This prevents JSON injection attacks via malformed metadata.
    /// </summary>
    private static string SanitizeMetadataJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || metadataJson == "{}")
        {
            return "{}";
        }

        try
        {
            // Parse and re-serialize to ensure valid JSON structure
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadataJson);
            if (parsed is null) return "{}";

            // Re-serialize with safe options
            return JsonSerializer.Serialize(parsed, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (JsonException)
        {
            // Invalid JSON - return empty object
            return "{}";
        }
    }
}
