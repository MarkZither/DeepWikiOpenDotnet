using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for PUT /api/wiki/{id} — updates the wiki description.
/// Wiki name is immutable after creation; include only description here.
/// Any "name" field in the request body will be rejected with 400.
/// </summary>
public class UpdateWikiRequest
{
    /// <summary>
    /// New description for the wiki. May be null to clear the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Captures any extra fields submitted by the client so the controller
    /// can detect and reject forbidden fields such as "name".
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
