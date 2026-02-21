using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Request payload for POST /api/documents/ingest.
/// Matches the IngestRequest schema used by the API service.
/// </summary>
public class IngestRequestDto
{
    [JsonPropertyName("documents")]
    public List<IngestDocumentDto> Documents { get; set; } = new();

    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; set; } = true;

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;
}
