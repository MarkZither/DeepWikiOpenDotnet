using DeepWiki.Rag.Core.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Ingestion;

/// <summary>
/// Tests for ChunkOptions configuration binding (T087).
///
/// Verifies that:
///   • ChunkSize, ChunkOverlap and MaxChunksPerFile bind from the
///     IConfiguration section "Embedding:Chunking"
///   • When keys are absent the defaults (512 / 128 / 200) are used
///
/// These tests target the FUTURE ChunkOptions record that will be created by
/// T091 and are expected to FAIL until that record exists.
/// </summary>
public class ChunkOptionsTests
{
    // =========================================================================
    // T087a — Values bind correctly from "Embedding:Chunking" config section
    // =========================================================================

    [Fact]
    public void ChunkOptions_BindsFromConfiguration_ReturnsConfiguredValues()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Chunking:ChunkSize"] = "1024",
                ["Embedding:Chunking:ChunkOverlap"] = "256",
                ["Embedding:Chunking:MaxChunksPerFile"] = "100"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ChunkOptions>(configuration.GetSection("Embedding:Chunking"));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<ChunkOptions>>().Value;

        // Assert
        Assert.Equal(1024, opts.ChunkSize);
        Assert.Equal(256, opts.ChunkOverlap);
        Assert.Equal(100, opts.MaxChunksPerFile);
    }

    // =========================================================================
    // T087b — Missing keys fall back to defaults (512 / 128 / 200)
    // =========================================================================

    [Fact]
    public void ChunkOptions_MissingKeys_FallBackToDefaults()
    {
        // Arrange — empty configuration, no Embedding:Chunking section at all
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var services = new ServiceCollection();
        services.Configure<ChunkOptions>(configuration.GetSection("Embedding:Chunking"));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<ChunkOptions>>().Value;

        // Assert — defaults must match the documented values
        Assert.Equal(512, opts.ChunkSize);
        Assert.Equal(128, opts.ChunkOverlap);
        Assert.Equal(200, opts.MaxChunksPerFile);
    }

    // =========================================================================
    // T087c — Partial config: only ChunkSize specified, others default
    // =========================================================================

    [Fact]
    public void ChunkOptions_PartialConfig_DefaultsAppliedForMissingKeys()
    {
        // Arrange — only ChunkSize overridden
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Chunking:ChunkSize"] = "768"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<ChunkOptions>(configuration.GetSection("Embedding:Chunking"));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<ChunkOptions>>().Value;

        // Assert
        Assert.Equal(768, opts.ChunkSize);
        Assert.Equal(128, opts.ChunkOverlap); // default
        Assert.Equal(200, opts.MaxChunksPerFile); // default
    }

    // =========================================================================
    // T087d — Validates the record has the correct defaults as constant values
    // =========================================================================

    [Fact]
    public void ChunkOptions_DefaultConstructor_HasExpectedDefaults()
    {
        // Act
        var opts = new ChunkOptions();

        // Assert
        Assert.Equal(512, opts.ChunkSize);
        Assert.Equal(128, opts.ChunkOverlap);
        Assert.Equal(200, opts.MaxChunksPerFile);
    }
}
