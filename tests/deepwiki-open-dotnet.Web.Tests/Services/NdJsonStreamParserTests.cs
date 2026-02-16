using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Services;
using Xunit;

namespace DeepWiki.Web.Tests.Services;

public class NdJsonStreamParserTests
{
    [Fact]
    public async Task ParseAsync_ValidNdjson_ReturnsDtos()
    {
        var ndjson = "{\"type\":\"token\",\"text\":\"Hello\"}\n{\"type\":\"done\",\"done\":true}\n";
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));

        var parser = new NdJsonStreamParser();
        var list = new System.Collections.Generic.List<global::deepwiki_open_dotnet.Web.Models.GenerationDeltaDto>();
        await foreach (var dto in parser.ParseAsync(ms))
        {
            list.Add(dto);
        }

        Assert.Equal(2, list.Count);
        Assert.Equal("Hello", list[0].Text);
        Assert.True(list[1].Done.HasValue && list[1].Done.Value);
    }

    [Fact]
    public async Task ParseAsync_Ignores_Empty_Lines_And_Returns_Error_For_Malformed()
    {
        var ndjson = "\n{\"type\":\"token\",\"text\":\"ok\"}\n{not-json}\n\n";
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));

        var parser = new NdJsonStreamParser();
        var list = await parser.ParseAsync(ms).ToListAsync();

        // Expect three yields: token, error (for malformed)
        Assert.True(list.Count >= 2);
        Assert.Equal("ok", list[0].Text);
        Assert.Equal("error", list[1].Type);
        Assert.NotNull(list[1].Error);
    }
}
