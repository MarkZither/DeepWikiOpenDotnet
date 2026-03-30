using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Generic paginated result envelope. Mirrors DeepWiki.ApiService.Models.PagedResult&lt;T&gt;.
/// </summary>
public class PagedResultDto<T>
{
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; set; } = [];

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}
