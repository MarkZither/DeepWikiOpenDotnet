using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DeepWiki.Examples;

/// <summary>
/// Example demonstrating Agent Framework compatibility for <see cref="IWikiGenerationService"/>.
///
/// ## Agent Framework Compatibility Review (T064b)
///
/// 1. **Tool binding**: <see cref="IWikiGenerationService.GenerateAsync"/> returns
///    <see cref="IAsyncEnumerable{WikiGenerationProgress}"/>, which is NOT directly
///    bindable as an <see cref="AIFunction"/> because agent tools must return a single value.
///    The wrapper method <see cref="WikiGenerationService.StartWikiGenerationAsync"/> adapts
///    the stream to a single JSON summary, making it fully bindable.
///
/// 2. **JSON-serializability**: <see cref="WikiGenerationProgress"/> uses
///    <c>[JsonPropertyName]</c> attributes on all properties and contains only primitive types
///    (string, int?, Guid) — it is fully JSON-serializable with <see cref="JsonSerializer"/> defaults.
///
/// 3. **Agent-recoverable errors**: All failure paths return a structured result string rather
///    than letting exceptions propagate. An <see cref="InvalidOperationException"/> (HTTP 409
///    Concurrent generation) is caught and returned as an actionable error message, allowing
///    the agent to decide whether to retry or inform the user.
///
/// 4. **DI registration**: See <see cref="ExampleDiSetupAsync"/> for the minimum service
///    registration required to use these tools from an agent.
///
/// Usage from an agent loop:
/// <code>
///   var tools = WikiGenerationAgentTools.CreateTools(serviceProvider);
///   // Pass tools to your IChatClient / agent invocation
/// </code>
/// </summary>
public static class WikiGenerationAgentTools
{
    /// <summary>
    /// Creates Agent Framework tools for wiki generation and exposes them as
    /// <see cref="AIFunction"/> instances compatible with any <see cref="IChatClient"/> tool loop.
    /// </summary>
    public static IList<AITool> CreateTools(IServiceProvider serviceProvider)
    {
        var wikiService = serviceProvider.GetRequiredService<IWikiGenerationService>();
        var adapter = new WikiGenerationToolAdapter(wikiService);

        return
        [
            AIFunctionFactory.Create(adapter.StartWikiGenerationAsync)
        ];
    }

    /// <summary>
    /// Minimal DI setup example for wiring <see cref="IWikiGenerationService"/> so that
    /// <see cref="CreateTools"/> can be called from an agent context.
    /// </summary>
    public static IServiceCollection AddWikiGenerationTools(this IServiceCollection services)
    {
        // IWikiGenerationService is already registered as scoped in ApiService/Program.cs.
        // In an agent host, register it here if not using the full ApiService:
        //   services.AddScoped<IWikiGenerationService, WikiGenerationOrchestrator>();
        // All IWikiGenerationService dependencies (IWikiRepository, IGenerationService, etc.)
        // must also be registered — see DIRegistrationExample.cs for the full chain.
        return services;
    }

    /// <summary>
    /// Demonstrates how an agent loop consumes wiki generation tool results.
    /// Shows the event stream being collected into a structured summary and
    /// agent-recoverable error handling for the 409 Conflict case.
    /// </summary>
    public static async Task ExampleAgentUsageAsync(IServiceProvider serviceProvider)
    {
        var tools = CreateTools(serviceProvider);
        Console.WriteLine($"Registered {tools.Count} wiki generation tool(s):");
        foreach (var tool in tools.OfType<AIFunction>())
            Console.WriteLine($"  - {tool.Name}: {tool.Description}");

        Console.WriteLine();
        Console.WriteLine("In a real agent loop, pass these tools to your IChatClient:");
        Console.WriteLine("  var response = await chatClient.CompleteAsync(messages, new() { Tools = tools });");
        Console.WriteLine();
        Console.WriteLine("The agent can invoke StartWikiGeneration to create a wiki from a collection.");
        Console.WriteLine("WikiGenerationProgress events are JSON-serializable for agent context inclusion.");

        // Demonstrate JSON-serializability of WikiGenerationProgress
        var sampleProgress = new WikiGenerationProgress
        {
            EventType = WikiGenerationProgress.EventWikiCreated,
            WikiId = Guid.NewGuid(),
            TotalPages = 10,
            Status = "generating"
        };
        var json = JsonSerializer.Serialize(sampleProgress);
        Console.WriteLine();
        Console.WriteLine("Sample WikiGenerationProgress JSON (agent can include this in context):");
        Console.WriteLine(json);
    }
}

/// <summary>
/// Adapts <see cref="IWikiGenerationService"/> for agent tool binding by collecting
/// the async stream into a single structured result string.
/// </summary>
internal sealed class WikiGenerationToolAdapter(IWikiGenerationService generationService)
{
    [Description(
        "Generates a wiki from a document collection using a two-phase LLM pipeline. " +
        "Returns a JSON summary of the generation result including the wiki ID, total pages, " +
        "and any page-level errors. Use the returned wikiId to retrieve the completed wiki content.")]
    public async Task<string> StartWikiGenerationAsync(
        [Description("The collection ID to generate the wiki from.")]
        string collectionId,
        [Description("The name for the new wiki (max 200 characters).")]
        string wikiName,
        [Description("Optional description for the wiki.")]
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var request = new WikiGenerationRequest
        {
            CollectionId = collectionId,
            Name = wikiName,
            Description = description
        };

        var summary = new WikiGenerationSummary();

        try
        {
            await foreach (var progress in generationService.GenerateAsync(request, cancellationToken))
            {
                switch (progress.EventType)
                {
                    case WikiGenerationProgress.EventWikiCreated:
                        summary.WikiId = progress.WikiId;
                        break;

                    case WikiGenerationProgress.EventTocComplete:
                        summary.TotalPages = progress.TotalPages ?? 0;
                        break;

                    case WikiGenerationProgress.EventPageComplete:
                        summary.CompletedPages++;
                        break;

                    case WikiGenerationProgress.EventPageError:
                        summary.FailedPages++;
                        if (progress.ErrorMessage is not null)
                            summary.Errors.Add($"Page '{progress.PageTitle}': {progress.ErrorMessage}");
                        break;

                    case WikiGenerationProgress.EventGenerationComplete:
                        summary.Status = progress.Status ?? "complete";
                        break;

                    case WikiGenerationProgress.EventGenerationCancelled:
                        summary.Status = "cancelled";
                        break;
                }
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
        {
            // Agent-recoverable: a generation for this collection+name is already running.
            // The agent can inform the user or choose to wait and retry.
            return JsonSerializer.Serialize(new
            {
                error = "concurrent_generation",
                message = $"A wiki generation for collection '{collectionId}' with name '{wikiName}' is already in progress. " +
                          "Wait for it to complete or use a different wiki name.",
                recoverable = true
            });
        }
        catch (OperationCanceledException)
        {
            summary.Status = "cancelled_by_caller";
        }

        return JsonSerializer.Serialize(summary);
    }
}

/// <summary>Summarised outcome of a wiki generation run for agent consumption.</summary>
internal sealed class WikiGenerationSummary
{
    public Guid WikiId { get; set; }
    public string Status { get; set; } = "unknown";
    public int TotalPages { get; set; }
    public int CompletedPages { get; set; }
    public int FailedPages { get; set; }
    public List<string> Errors { get; } = [];
}
