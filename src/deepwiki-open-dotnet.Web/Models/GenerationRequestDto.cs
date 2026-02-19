using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

public class GenerationRequestDto
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("topK")]
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Optional list of document collection IDs to restrict retrieval scope.
    /// When null or empty the backend searches across all collections.
    /// </summary>
    [JsonPropertyName("collection_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CollectionIds { get; init; }

    [JsonPropertyName("filters")]
    public Dictionary<string, string>? Filters { get; init; }

    [JsonPropertyName("context")]
    public List<ContextMessageDto>? Context { get; init; }
}
