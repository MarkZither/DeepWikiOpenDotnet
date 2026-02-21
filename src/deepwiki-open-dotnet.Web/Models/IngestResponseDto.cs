using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Response from POST /api/documents/ingest.
/// Matches the IngestResponse schema returned by the API service.
/// </summary>
public class IngestResponseDto
{
    [JsonPropertyName("successCount")]
    public int SuccessCount { get; set; }

    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("ingestedDocumentIds")]
    public List<Guid> IngestedDocumentIds { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<IngestErrorDto> Errors { get; set; } = new();
}

/// <summary>
/// Details of a single document ingestion failure.
/// </summary>
public class IngestErrorDto
{
    [JsonPropertyName("documentIdentifier")]
    public string DocumentIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;
}
