using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

public class GenerationDeltaDto
{
    [JsonPropertyName("promptId")]
    public string? PromptId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "token", "done", "error"

    [JsonPropertyName("seq")]
    public int Sequence { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("done")]
    public bool? Done { get; init; }

    [JsonPropertyName("metadata")]
    public GenerationMetadataDto? Metadata { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public class GenerationMetadataDto
{
    [JsonPropertyName("sources")]
    public List<SourceDocumentDto>? Sources { get; init; }

    [JsonPropertyName("retrievalTimeMs")]
    public double? RetrievalTimeMs { get; init; }

    [JsonPropertyName("totalTokens")]
    public int? TotalTokens { get; init; }
}

public class SourceDocumentDto
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; init; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; init; }

    [JsonPropertyName("score")]
    public float Score { get; init; }
}
