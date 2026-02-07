using System.Collections.Generic;
using System.Text;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Streaming;

public class StreamNormalizer
{
    private readonly string _promptId;
    private readonly string _role;

    public StreamNormalizer(string promptId, string role)
    {
        _promptId = promptId;
        _role = role;
    }

    /// <summary>
    /// Normalize incoming byte chunks into a sequence of GenerationDelta token events.
    /// Deduplicates consecutive identical text chunks and ensures UTF-8 safety across chunk boundaries.
    /// </summary>
    public IEnumerable<GenerationDelta> Normalize(IEnumerable<byte[]> chunks)
    {
        if (chunks == null)
            yield break;

        var decoder = Encoding.UTF8.GetDecoder();
        var seq = 0;
        string? lastEmitted = null;

        foreach (var chunk in chunks)
        {
            if (chunk == null || chunk.Length == 0)
                continue;

            // Use decoder to convert bytes to chars, preserving state across chunks
            var charCount = decoder.GetCharCount(chunk, 0, chunk.Length);
            var chars = new char[charCount];
            decoder.GetChars(chunk, 0, chunk.Length, chars, 0);
            var text = new string(chars);

            if (string.IsNullOrEmpty(text))
                continue;

            // Deduplicate consecutive identical texts
            if (lastEmitted != null && lastEmitted == text)
                continue;

            lastEmitted = text;

            yield return new GenerationDelta
            {
                PromptId = _promptId,
                Role = _role,
                Type = "token",
                Seq = seq++,
                Text = text
            };
        }
    }
}
