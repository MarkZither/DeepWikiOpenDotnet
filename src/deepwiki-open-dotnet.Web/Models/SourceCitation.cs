namespace deepwiki_open_dotnet.Web.Models;

public class SourceCitation
{
    public string Title { get; init; } = string.Empty;
    public string? RepoUrl { get; init; }
    public string? FilePath { get; init; }
    public string? Excerpt { get; init; }
    public string? Url { get; init; }
    public float Score { get; init; }
}
