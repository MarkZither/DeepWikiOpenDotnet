using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DeepWiki.Rag.Core.Snapshots;

/// <summary>
/// Writes each <see cref="WikiSnapshotEntry"/> as a formatted JSON file under <see cref="WikiSnapshotOptions.OutputPath"/>.
/// File names follow the pattern <c>{feature}-{id}.json</c>.
/// </summary>
public sealed class WikiSnapshotRecorder : IWikiSnapshotRecorder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _outputPath;

    public WikiSnapshotRecorder(IOptions<WikiSnapshotOptions> options)
    {
        _outputPath = options.Value.OutputPath;
    }

    /// <inheritdoc/>
    public async Task RecordAsync(WikiSnapshotEntry entry, CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(_outputPath);
        Directory.CreateDirectory(dir);

        var safeName = string.Join("-", entry.Feature.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(dir, $"{safeName}-{entry.Id}.json");

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await JsonSerializer.SerializeAsync(stream, entry, SerializerOptions, ct);
    }

    /// <summary>
    /// Computes a lowercase hex SHA-256 hash of the given UTF-8 text.
    /// Use this to populate <see cref="WikiSnapshotEntry.ResponseHash"/>.
    /// </summary>
    public static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
