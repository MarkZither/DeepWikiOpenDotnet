using System;
using System.Collections.Generic;

namespace deepwiki_open_dotnet.Web.Models;

public class ChatMessageModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public MessageRole Role { get; init; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsStreaming { get; set; }
    public List<SourceCitation> Sources { get; init; } = new();
    public string? ErrorMessage { get; set; }
}
