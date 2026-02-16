using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

public class SessionRequestDto
{
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }
}
