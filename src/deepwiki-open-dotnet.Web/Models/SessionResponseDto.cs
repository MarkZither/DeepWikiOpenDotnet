using System;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

public class SessionResponseDto
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }
}
