using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Two-phase wiki generation orchestrator.
/// Phase 1: calls IGenerationService with a TOC prompt to produce a structured outline.
/// Phase 2: calls IGenerationService for each page, using RAG-retrieved context.
/// Emits <see cref="WikiGenerationProgress"/> events as an NDJSON-streamable async sequence.
/// </summary>
public class WikiGenerationOrchestrator : IWikiGenerationService
{
    private readonly IWikiRepository _repository;
    private readonly IGenerationService _generationService;
    private readonly SessionManager _sessionManager;
    private readonly IVectorStore? _vectorStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly WikiGenerationOptions _options;
    private readonly WikiTocParser _tocParser = new();
    private readonly WikiPageParser _pageParser = new();
    private readonly ILogger<WikiGenerationOrchestrator>? _logger;
    private readonly IWikiProgressNotifier? _progressNotifier;

    /// <summary>In-process concurrent generation guard: prevents double-triggering for the same collection+name pair.</summary>
    private static readonly ConcurrentDictionary<string, bool> _activeGenerations = new();

    private static readonly string TocPromptTemplate = LoadEmbeddedResource("DeepWiki.Rag.Core.Prompts.wiki-toc-prompt.txt");
    private static readonly string PagePromptTemplate = LoadEmbeddedResource("DeepWiki.Rag.Core.Prompts.wiki-page-prompt.txt");

    public WikiGenerationOrchestrator(
        IWikiRepository repository,
        IGenerationService generationService,
        SessionManager sessionManager,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null,
        IOptions<WikiGenerationOptions>? options = null,
        ILogger<WikiGenerationOrchestrator>? logger = null,
        IWikiProgressNotifier? progressNotifier = null)
    {
        _repository = repository;
        _generationService = generationService;
        _sessionManager = sessionManager;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _options = options?.Value ?? new WikiGenerationOptions();
        _logger = logger;
        _progressNotifier = progressNotifier;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This synchronous method checks the concurrent generation guard BEFORE returning the async enumerable.
    /// An <see cref="InvalidOperationException"/> is thrown synchronously (before any yield) when a duplicate
    /// generation is detected, mapping to HTTP 409 Conflict in the controller.
    /// </remarks>
    public IAsyncEnumerable<WikiGenerationProgress> GenerateAsync(
        WikiGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CollectionId))
            throw new ArgumentException("CollectionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.", nameof(request));

        // ── Synchronous concurrent guard ─────────────────────────────────────
        // Check is done here (outside the async iterator) so the exception is thrown
        // synchronously, enabling the controller to return 409 before any streaming starts.
        var key = BuildGuardKey(request.CollectionId, request.Name);

        // Check in-memory guard first (fastest)
        if (!_activeGenerations.TryAdd(key, true))
            throw new InvalidOperationException(
                $"A wiki generation is already in progress for collection '{request.CollectionId}' and name '{request.Name}'.");

        // Check database for in-progress generation (covers multi-instance deployments)
        bool existsInDb;
        try
        {
            existsInDb = _repository.ExistsGeneratingAsync(request.CollectionId, request.Name, cancellationToken)
                .GetAwaiter().GetResult();
        }
        catch
        {
            _activeGenerations.TryRemove(key, out _);
            throw;
        }

        if (existsInDb)
        {
            _activeGenerations.TryRemove(key, out _);
            throw new InvalidOperationException(
                $"A wiki named '{request.Name}' is already being generated from collection '{request.CollectionId}'.");
        }

        return GenerateAsyncCore(request, key, cancellationToken);
    }

    private async IAsyncEnumerable<WikiGenerationProgress> GenerateAsyncCore(
        WikiGenerationRequest request,
        string guardKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // C# prohibits yield inside try/catch blocks (CS1626/CS1631).
        // Solution: use an unbounded Channel as a pipe between the producer
        // (RunGenerationAsync) that owns all try/catch logic and this iterator
        // that simply yields whatever the producer writes.
        var channel = Channel.CreateUnbounded<WikiGenerationProgress>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        // Producer runs in the background and terminates the channel when done.
        // It must never throw — exceptions are conveyed via writer.Complete(ex).
        var producerTask = RunGenerationAsync(channel.Writer, request, guardKey, cancellationToken);

        // Read events until the producer closes the channel (CancellationToken.None
        // ensures we drain the final cancelled/error event even after caller cancels).
        await foreach (var progress in channel.Reader.ReadAllAsync(CancellationToken.None))
            yield return progress;

        // Re-throw unexpected escaped exceptions (e.g. OOM, Stack Overflow).
        await producerTask;
    }

    private async Task RunGenerationAsync(
        ChannelWriter<WikiGenerationProgress> writer,
        WikiGenerationRequest request,
        string guardKey,
        CancellationToken cancellationToken)
    {
        var session = _sessionManager.CreateSession("wiki-generation");
        var sessionId = session.SessionId;

        WikiEntity? wiki = null;
        var hasErrors = false;

        try
        {
            // ── Create wiki shell (Status: Generating) ─────────────────────────
            var now = DateTime.UtcNow;
            wiki = await _repository.CreateWikiAsync(new WikiEntity
            {
                Id = Guid.NewGuid(),
                CollectionId = request.CollectionId,
                Name = request.Name,
                Description = request.Description,
                Status = WikiStatus.Generating,
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken);

            _logger?.LogInformation(
                "[Wiki] Starting generation for '{WikiName}' (id: {WikiId}, collection: {CollectionId})",
                wiki.Name, wiki.Id, wiki.CollectionId);

            await EmitAsync(writer, Progress(WikiGenerationProgress.EventWikiCreated, wiki.Id));

            // ── Phase 1: TOC Generation ────────────────────────────────────────
            _logger?.LogInformation("[Wiki] Phase 1 — calling LLM for table of contents (wiki: {WikiId})", wiki.Id);
            var tocEntries = await GenerateTocWithRetryAsync(
                wiki, sessionId, request.CollectionId, writer, cancellationToken);

            // Persist empty page stubs (Status: Generating)
            var pageEntities = new List<WikiPageEntity>();
            for (var i = 0; i < tocEntries.Count; i++)
            {
                var entry = tocEntries[i];
                var stub = new WikiPageEntity
                {
                    Id = Guid.NewGuid(),
                    WikiId = wiki.Id,
                    Title = entry.PageTitle,
                    Content = string.Empty,
                    SectionPath = entry.SectionPath,
                    SortOrder = i,
                    Status = PageStatus.Generating,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                var persisted = await _repository.UpsertPageAsync(stub, cancellationToken);
                pageEntities.Add(persisted);
            }

            var tocJson = BuildTocJson(tocEntries);
            _logger?.LogInformation(
                "[Wiki] Phase 1 complete — TOC has {PageCount} pages for wiki '{WikiName}' (id: {WikiId})",
                tocEntries.Count, wiki.Name, wiki.Id);

            await EmitAsync(writer, new WikiGenerationProgress
            {
                EventType = WikiGenerationProgress.EventTocComplete,
                WikiId = wiki.Id,
                TotalPages = tocEntries.Count
            });

            // ── Phase 2: Page Content Generation ──────────────────────────────
            var pageIdByTitle = pageEntities.ToDictionary(
                p => p.Title, p => p.Id, StringComparer.OrdinalIgnoreCase);

            if (_options.Mode == "parallel" && _options.MaxParallelPages > 1)
            {
                _logger?.LogInformation(
                    "[Wiki] Phase 2 — generating {PageCount} pages in parallel (max {MaxParallel}) for wiki '{WikiName}'",
                    tocEntries.Count, _options.MaxParallelPages, wiki.Name);
                var parallelResults = await GeneratePagesParallelAsync(
                    wiki, sessionId, tocEntries, pageEntities, tocJson, pageIdByTitle, cancellationToken);

                foreach (var evt in parallelResults.Events)
                    await EmitAsync(writer, evt);

                hasErrors = parallelResults.HasErrors;
            }
            else
            {
                // Sequential mode
                _logger?.LogInformation(
                    "[Wiki] Phase 2 — generating {PageCount} pages sequentially for wiki '{WikiName}'",
                    tocEntries.Count, wiki.Name);
                for (var i = 0; i < tocEntries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = tocEntries[i];
                    var pageEntity = pageEntities[i];

                    await EmitAsync(writer, new WikiGenerationProgress
                    {
                        EventType = WikiGenerationProgress.EventPageStart,
                        WikiId = wiki.Id,
                        PageIndex = i,
                        TotalPages = tocEntries.Count,
                        PageTitle = entry.PageTitle
                    });

                    var (pageSucceeded, pageProgress) = await GenerateSinglePageAsync(
                        wiki, sessionId, entry, pageEntity, tocJson, pageIdByTitle, i, tocEntries.Count, cancellationToken);

                    foreach (var evt in pageProgress)
                        await EmitAsync(writer, evt);

                    if (!pageSucceeded)
                        hasErrors = true;
                }
            }

            // ── Finalise wiki status ────────────────────────────────────────────
            var finalStatus = hasErrors ? WikiStatus.Partial : WikiStatus.Complete;
            await _repository.UpdateWikiStatusAsync(wiki.Id, finalStatus, cancellationToken);

            _logger?.LogInformation(
                "[Wiki] Generation finished for '{WikiName}' (id: {WikiId}) — status: {Status}",
                wiki.Name, wiki.Id, finalStatus);

            await EmitAsync(writer, new WikiGenerationProgress
            {
                EventType = WikiGenerationProgress.EventGenerationComplete,
                WikiId = wiki.Id,
                Status = finalStatus.ToString()
            });
        }
        catch (OperationCanceledException)
        {
            if (wiki is not null)
            {
                try
                {
                    await _repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Partial, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to update wiki {WikiId} status to Partial after cancellation.", wiki.Id);
                }
            }

            await EmitAsync(writer, new WikiGenerationProgress
            {
                EventType = WikiGenerationProgress.EventGenerationCancelled,
                WikiId = wiki?.Id ?? Guid.Empty,
                ErrorMessage = "Generation was cancelled by the caller.",
                Status = WikiStatus.Partial.ToString()
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger?.LogError(ex, "[Wiki] Generation FAILED for wiki '{Name}' (id: {WikiId}) — {ErrorMessage}",
                request.Name, wiki?.Id, ex.Message);

            if (wiki is not null)
            {
                try
                {
                    await _repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Error, CancellationToken.None);
                }
                catch { /* best effort */ }
            }

            // Write an explicit error event before closing the channel so the client
            // receives a terminal event and can show a failure state rather than hanging.
            await EmitAsync(writer, new WikiGenerationProgress
            {
                EventType    = WikiGenerationProgress.EventGenerationCancelled,
                WikiId       = wiki?.Id ?? Guid.Empty,
                ErrorMessage = $"Generation failed: {ex.Message}",
                Status       = WikiStatus.Error.ToString()
            });

            writer.Complete();
            return;
        }
        finally
        {
            _activeGenerations.TryRemove(guardKey, out _);
        }

        writer.Complete();
    }

    // ── Phase 1: TOC with retry ──────────────────────────────────────────────

    private async Task<IReadOnlyList<TocEntry>> GenerateTocWithRetryAsync(
        WikiEntity wiki,
        string sessionId,
        string collectionId,
        ChannelWriter<WikiGenerationProgress> writer,
        CancellationToken ct)
    {
        var retrievalSw = Stopwatch.StartNew();
        await EmitAsync(writer, StatusUpdate(wiki.Id, "Retrieving document context from vector store…"));
        var documentSummaries = await GetDocumentSummariesAsync(collectionId, ct);
        retrievalSw.Stop();

        var maxPages = Math.Max(5, _options.PageTokenLimit / 200); // rough heuristic

        var tocPrompt = TocPromptTemplate
            .Replace("{document_summaries}", documentSummaries)
            .Replace("{max_pages}", maxPages.ToString());

        _logger?.LogInformation(
            "[Wiki] TOC prompt ready — {PromptChars} chars, doc-summaries {SummaryChars} chars, retrieval {RetrievalMs}ms",
            tocPrompt.Length, documentSummaries.Length, retrievalSw.ElapsedMilliseconds);

        await EmitAsync(writer, StatusUpdate(wiki.Id, $"Document context ready — building table of contents prompt ({retrievalSw.ElapsedMilliseconds}ms)"));

        var maxAttempts = _options.MaxTocRetries + 1;
        WikiTocParseException? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _logger?.LogInformation(
                "[Wiki] TOC LLM call — attempt {Attempt}/{Max} for wiki '{WikiName}' (collection: {CollectionId})",
                attempt + 1, maxAttempts, wiki.Name, collectionId);

            await EmitAsync(writer, StatusUpdate(wiki.Id,
                attempt == 0
                    ? "Calling LLM for table of contents — this may take several minutes…"
                    : $"Retrying table of contents (attempt {attempt + 1}/{maxAttempts})…"));

            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            var tokenCount = 0;
            var firstToken = true;
            await foreach (var delta in _generationService.GenerateAsync(
                sessionId, tocPrompt, topK: 0, cancellationToken: ct))
            {
                if (delta.Type == "token" && delta.Text is not null)
                {
                    if (firstToken)
                    {
                        firstToken = false;
                        _logger?.LogInformation(
                            "[Wiki] TOC first token received after {ElapsedMs}ms (attempt {Attempt}/{Max})",
                            sw.ElapsedMilliseconds, attempt + 1, maxAttempts);
                        await EmitAsync(writer, StatusUpdate(wiki.Id, $"LLM responding — generating table of contents… (first token after {sw.ElapsedMilliseconds}ms)"));
                    }
                    sb.Append(delta.Text);
                    tokenCount++;
                    if (tokenCount % 50 == 0)
                    {
                        _logger?.LogDebug(
                            "[Wiki] TOC streaming — {Tokens} tokens so far, {ElapsedMs}ms elapsed (attempt {Attempt}/{Max})",
                            tokenCount, sw.ElapsedMilliseconds, attempt + 1, maxAttempts);
                        await EmitAsync(writer, new WikiGenerationProgress
                        {
                            EventType  = WikiGenerationProgress.EventTocToken,
                            WikiId     = wiki.Id,
                            TokenCount = tokenCount
                        });
                    }
                }
            }
            sw.Stop();

            var response = sb.ToString();
            _logger?.LogInformation(
                "[Wiki] TOC LLM response complete — attempt {Attempt}/{Max}, {Tokens} tokens, {Chars} chars in {ElapsedMs}ms",
                attempt + 1, maxAttempts, tokenCount, response.Length, sw.ElapsedMilliseconds);

            try
            {
                var entries = _tocParser.Parse(response);
                _logger?.LogInformation(
                    "[Wiki] TOC parsed successfully — {PageCount} pages for wiki '{WikiName}'",
                    entries.Count, wiki.Name);
                return entries;
            }
            catch (WikiTocParseException ex)
            {
                _logger?.LogWarning(ex, "TOC parse failed on attempt {Attempt}/{Max}. Response: {Response}",
                    attempt + 1, maxAttempts, response.Length > 500 ? response[..500] : response);
                lastException = ex;
            }
        }

        throw lastException ?? new WikiTocParseException("TOC generation failed after all retries.");
    }

    // ── Phase 2: Single page ─────────────────────────────────────────────────

    private async Task<(bool Succeeded, IReadOnlyList<WikiGenerationProgress> Events)> GenerateSinglePageAsync(
        WikiEntity wiki,
        string sessionId,
        TocEntry entry,
        WikiPageEntity pageEntity,
        string tocJson,
        Dictionary<string, Guid> pageIdByTitle,
        int pageIndex,
        int totalPages,
        CancellationToken ct)
    {
        var events = new List<WikiGenerationProgress>();

        try
        {
            var documentChunks = await GetPageChunksAsync(
                entry.PageTitle, entry.SectionPath, entry.Keywords, ct);

            var pagePrompt = PagePromptTemplate
                .Replace("{wiki_name}", wiki.Name)
                .Replace("{section_path}", entry.SectionPath)
                .Replace("{page_title}", entry.PageTitle)
                .Replace("{toc_json}", tocJson)
                .Replace("{document_chunks}", documentChunks);

            _logger?.LogInformation(
                "[Wiki] Page LLM call starting — [{PageNum}/{Total}] '{PageTitle}' (wiki: {WikiId})",
                pageIndex + 1, totalPages, entry.PageTitle, wiki.Id);

            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            await foreach (var delta in _generationService.GenerateAsync(
                sessionId, pagePrompt, topK: 0, cancellationToken: ct))
            {
                if (delta.Type == "token" && delta.Text is not null)
                {
                    sb.Append(delta.Text);
                    events.Add(new WikiGenerationProgress
                    {
                        EventType = WikiGenerationProgress.EventPageToken,
                        WikiId = wiki.Id,
                        PageIndex = pageIndex,
                        TotalPages = totalPages,
                        TokenText = delta.Text
                    });
                }
            }
            sw.Stop();

            var fullResponse = sb.ToString();
            _logger?.LogInformation(
                "[Wiki] Page LLM call complete — [{PageNum}/{Total}] '{PageTitle}' — {Chars} chars in {ElapsedMs}ms",
                pageIndex + 1, totalPages, entry.PageTitle, fullResponse.Length, sw.ElapsedMilliseconds);
            var (content, relatedTitles) = _pageParser.Parse(fullResponse);

            // Upsert page with content and OK status
            pageEntity.Content = content;
            pageEntity.Status = PageStatus.OK;
            pageEntity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpsertPageAsync(pageEntity, ct);

            // Resolve related page IDs and persist relations
            var relatedIds = relatedTitles
                .Where(t => pageIdByTitle.ContainsKey(t))
                .Select(t => pageIdByTitle[t])
                .Where(id => id != pageEntity.Id)
                .Distinct()
                .ToList();

            if (relatedIds.Count > 0)
                await _repository.SetRelatedPagesAsync(pageEntity.Id, relatedIds, ct);

            events.Add(new WikiGenerationProgress
            {
                EventType = WikiGenerationProgress.EventPageComplete,
                WikiId = wiki.Id,
                PageIndex = pageIndex,
                TotalPages = totalPages,
                PageTitle = entry.PageTitle
            });

            return (true, events);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning(
                "[Wiki] Page generation cancelled — [{PageNum}/{Total}] '{PageTitle}'",
                pageIndex + 1, totalPages, entry.PageTitle);
            throw; // propagate cancellation
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[Wiki] Page generation FAILED — [{PageNum}/{Total}] '{PageTitle}': {ErrorMessage}",
                pageIndex + 1, totalPages, entry.PageTitle, ex.Message);

            // Mark page as Error and continue
            try
            {
                pageEntity.Status = PageStatus.Error;
                pageEntity.UpdatedAt = DateTime.UtcNow;
                await _repository.UpsertPageAsync(pageEntity, CancellationToken.None);
            }
            catch (Exception persistEx)
            {
                _logger?.LogError(persistEx, "Failed to persist Error status for page '{PageTitle}'.", entry.PageTitle);
            }

            events.Add(new WikiGenerationProgress
            {
                EventType = WikiGenerationProgress.EventPageError,
                WikiId = wiki.Id,
                PageIndex = pageIndex,
                TotalPages = totalPages,
                PageTitle = entry.PageTitle,
                ErrorMessage = ex.Message
            });

            return (false, events);
        }
    }

    // ── Parallel page generation ─────────────────────────────────────────────

    private async Task<(IReadOnlyList<WikiGenerationProgress> Events, bool HasErrors)> GeneratePagesParallelAsync(
        WikiEntity wiki,
        string sessionId,
        IReadOnlyList<TocEntry> tocEntries,
        IReadOnlyList<WikiPageEntity> pageEntities,
        string tocJson,
        Dictionary<string, Guid> pageIdByTitle,
        CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(_options.MaxParallelPages, _options.MaxParallelPages);
        var allEvents = new ConcurrentDictionary<int, IReadOnlyList<WikiGenerationProgress>>();
        var hasErrors = false;

        var tasks = tocEntries.Select(async (entry, i) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var pageEntity = pageEntities[i];
                var startEvent = new WikiGenerationProgress
                {
                    EventType = WikiGenerationProgress.EventPageStart,
                    WikiId = wiki.Id,
                    PageIndex = i,
                    TotalPages = tocEntries.Count,
                    PageTitle = entry.PageTitle
                };

                var (succeeded, pageEvents) = await GenerateSinglePageAsync(
                    wiki, sessionId, entry, pageEntity, tocJson, pageIdByTitle, i, tocEntries.Count, ct);

                var combined = new[] { startEvent }.Concat(pageEvents).ToList();
                allEvents[i] = combined;

                if (!succeeded)
                    Volatile.Write(ref hasErrors, true);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Reconstruct events in page-index order
        var orderedEvents = allEvents.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).ToList();
        return (orderedEvents, hasErrors);
    }

    // ── RAG helpers ──────────────────────────────────────────────────────────

    private async Task<string> GetDocumentSummariesAsync(string collectionId, CancellationToken ct)
    {
        if (_vectorStore is null || _embeddingService is null)
            return $"Collection: {collectionId}";

        try
        {
            var embedding = await _embeddingService.EmbedAsync(collectionId, ct);
            var results = await _vectorStore.QueryAsync(embedding, k: 20,
                filters: new Dictionary<string, string> { { "repoUrl", collectionId } }, ct);

            if (results.Count == 0)
                return $"Collection: {collectionId} (no documents found)";

            var sb = new StringBuilder();
            foreach (var r in results)
            {
                var snippet = r.Document.Text.Length > 200 ? r.Document.Text[..200] : r.Document.Text;
                sb.AppendLine($"- {r.Document.FilePath ?? "Document"}: {snippet}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wiki] Vector store retrieval FAILED for collection '{CollectionId}' — TOC will have no document context.", collectionId);
            return $"Collection: {collectionId}";
        }
    }

    private async Task<string> GetPageChunksAsync(
        string pageTitle, string sectionPath, IReadOnlyList<string> keywords, CancellationToken ct)
    {
        if (_vectorStore is null || _embeddingService is null)
            return string.Empty;

        try
        {
            var queryText = $"{pageTitle} {sectionPath} {string.Join(" ", keywords)}";
            var embedding = await _embeddingService.EmbedAsync(queryText, ct);
            var results = await _vectorStore.QueryAsync(embedding, k: 5, cancellationToken: ct);

            if (results.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (var i = 0; i < results.Count; i++)
            {
                sb.AppendLine($"[{i + 1}] {results[i].Document.FilePath ?? "Document"}: {results[i].Document.Text}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Vector store query failed for page '{PageTitle}'.", pageTitle);
            return string.Empty;
        }
    }

    // ── Emit helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a progress event to the channel for HTTP/NDJSON consumers AND fires a
    /// fire-and-forget SignalR notification via <see cref="IWikiProgressNotifier"/>.
    /// SignalR errors are swallowed so they never kill the generation pipeline.
    /// </summary>
    private async ValueTask EmitAsync(ChannelWriter<WikiGenerationProgress> writer, WikiGenerationProgress progress)
    {
        await writer.WriteAsync(progress, CancellationToken.None);
        if (_progressNotifier is not null)
        {
            try
            {
                await _progressNotifier.NotifyAsync(progress);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Wiki] SignalR notification failed for event {EventType} on wiki {WikiId} — generation continues.",
                    progress.EventType, progress.WikiId);
            }
        }
    }

    // ── Utilities ──────────────────────────────────────────────────────────

    private static string BuildGuardKey(string collectionId, string name) =>
        $"{collectionId.ToLowerInvariant()}::{name.ToLowerInvariant()}";

    private static WikiGenerationProgress Progress(string eventType, Guid wikiId) =>
        new() { EventType = eventType, WikiId = wikiId };

    private static WikiGenerationProgress StatusUpdate(Guid wikiId, string message) =>
        new() { EventType = WikiGenerationProgress.EventStatusUpdate, WikiId = wikiId, Message = message };

    private static string BuildTocJson(IReadOnlyList<TocEntry> entries)
    {
        var sections = entries
            .GroupBy(e => e.SectionPath)
            .Select(g => new
            {
                sectionPath = g.Key,
                pages = g.Select(e => new { title = e.PageTitle }).ToList()
            })
            .ToList();

        return JsonSerializer.Serialize(new { sections });
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(WikiGenerationOrchestrator).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
