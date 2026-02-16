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

    [JsonPropertyName("filters")]
    public Dictionary<string, string>? Filters { get; init; }

    [JsonPropertyName("context")]
    public List<ContextMessageDto>? Context { get; init; }
}
