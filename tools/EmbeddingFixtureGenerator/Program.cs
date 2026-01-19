using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace DeepWiki.EmbeddingFixtureGenerator;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task<int> Main(string[] args)
    {
        var config = ParseArgs(args);

        AnsiConsole.MarkupLine("[bold green]Embedding Fixture Generator[/] \n");

        AnsiConsole.MarkupLine($"Host: [yellow]{config.Host}[/], Port: [yellow]{config.Port}[/], Model: [yellow]{config.Model}[/], Ollama: [yellow]{config.UseOllama}[/]");

        if (!File.Exists(config.InputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Input file not found: [yellow]{config.InputPath}[/]");
            return 2;
        }

        var docsJson = await File.ReadAllTextAsync(config.InputPath);
        var docs = JsonSerializer.Deserialize<List<DocumentInput>>(docsJson, JsonOptions) ?? new();

        if (!docs.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No documents found in input file.[/]");
            return 1;
        }

        var client = new EmbeddingClient(config.Host, config.Port, config.UseOllama, config.Model);

        var results = new List<EmbeddingOutput>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Generating embeddings", maxValue: docs.Count);
                foreach (var doc in docs)
                {
                    try
                    {
                        var emb = await client.GetEmbeddingAsync(doc.Text);
                        results.Add(new EmbeddingOutput { Id = doc.Id, Embedding = emb });
                        task.Increment(1);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to embed document {doc.Id}:[/] {ex.Message}");
                    }
                }
            });

        var outJson = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(config.OutputPath, outJson);

        AnsiConsole.MarkupLine($"\n[green]Wrote {results.Count} embeddings to[/] [yellow]{config.OutputPath}[/]");
        return 0;
    }

    private static Config ParseArgs(string[] args)
    {
        var cfg = new Config();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--host":
                    cfg.Host = args[++i];
                    break;
                case "--port":
                    if (int.TryParse(args[++i], out var p)) cfg.Port = p;
                    break;
                case "--model":
                    cfg.Model = args[++i];
                    break;
                case "--input":
                    cfg.InputPath = args[++i];
                    break;
                case "--output":
                    cfg.OutputPath = args[++i];
                    break;
                case "--ollama":
                    cfg.UseOllama = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    // ignore unknown for now
                    break;
            }
        }

        return cfg;
    }

    private static void PrintHelp()
    {
        AnsiConsole.Write(new FigletText("EmbeddingGenerator").Centered().Color(Color.Green));
        AnsiConsole.MarkupLine("\nOptions:\n");
        AnsiConsole.MarkupLine("  --host <host>       Host (default: localhost)");
        AnsiConsole.MarkupLine("  --port <port>       Port (default: 5273 for foundry; 11434 for ollama)");
        AnsiConsole.MarkupLine("  --model <model>     Model name (default: mxbai-embed-large or nomic-embed-text for Ollama)");
        AnsiConsole.MarkupLine("  --input <path>      Input JSON file with documents (default: tests/.../sample-documents.json)");
        AnsiConsole.MarkupLine("  --output <path>     Output JSON file path (default: tests/.../sample-embeddings.json)");
        AnsiConsole.MarkupLine("  --ollama            Use Ollama API (default: Foundry-compatible /v1/embeddings endpoint)");
    }
}

internal record Config
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5273;
    public bool UseOllama { get; set; }
    public string Model { get; set; } = "mxbai-embed-large";
    public string InputPath { get; set; } = Path.Combine("tests", "DeepWiki.Rag.Core.Tests", "fixtures", "embedding-samples", "sample-documents.json");
    public string OutputPath { get; set; } = Path.Combine("tests", "DeepWiki.Rag.Core.Tests", "fixtures", "embedding-samples", "sample-embeddings.json");
}

internal class DocumentInput
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Title { get; set; }
}

internal class EmbeddingOutput
{
    public string Id { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
