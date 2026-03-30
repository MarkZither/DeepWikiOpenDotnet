using System.ComponentModel.DataAnnotations;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for POST /api/wiki/export.
/// </summary>
public sealed class WikiExportRequest
{
    /// <summary>ID of the wiki to export.</summary>
    [Required]
    public Guid WikiId { get; set; }

    /// <summary>
    /// Export format. Accepted values: <c>markdown</c>, <c>json</c>.
    /// </summary>
    [Required]
    [RegularExpression(
        "^(markdown|json)$",
        ErrorMessage = "Format must be 'markdown' or 'json'.")]
    public string Format { get; set; } = string.Empty;
}
