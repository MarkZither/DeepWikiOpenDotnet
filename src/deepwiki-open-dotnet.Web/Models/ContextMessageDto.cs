using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

public class ContextMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty; // "user" or "assistant"

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}
