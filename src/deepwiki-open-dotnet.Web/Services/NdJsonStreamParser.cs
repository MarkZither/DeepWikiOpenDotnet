using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

public sealed class NdJsonStreamParser
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parse an NDJSON stream line-by-line into GenerationDeltaDto objects.
    /// Malformed JSON lines are converted into a GenerationDeltaDto with Type="error" and the Error set.
    /// Empty/whitespace lines are ignored.
    /// </summary>
    public async IAsyncEnumerable<GenerationDeltaDto> ParseAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            GenerationDeltaDto? dto = null;
            string? parseError = null;

            try
            {
                dto = JsonSerializer.Deserialize<GenerationDeltaDto>(line, _jsonOptions);
            }
            catch (JsonException je)
            {
                parseError = je.Message;
            }

            if (parseError is not null)
            {
                yield return new GenerationDeltaDto
                {
                    Type = "error",
                    Error = $"ndjson parse error: {parseError}",
                    Text = line
                };

                continue;
            }

            if (dto is not null)
                yield return dto;
        }
    }
}
