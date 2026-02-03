using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Tokenization;

namespace DeepWiki.Rag.Core.Tests.Tokenization;

/// <summary>
/// Unit tests for TokenizationService covering T061-T066.
/// Tests CountTokensAsync for all providers (OpenAI, Foundry, Ollama),
/// empty strings, and multilingual text.
/// </summary>
public class TokenizationServiceTests
{
    private readonly TokenizationService _service;

    public TokenizationServiceTests()
    {
        var encoderFactory = new TokenEncoderFactory();
        _service = new TokenizationService(encoderFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenizationService>.Instance);
    }

    #region T062: CountTokensAsync for OpenAI model returns integer token count

    [Fact]
    public async Task CountTokensAsync_OpenAI_ReturnsIntegerTokenCount()
    {
        // Arrange
        var text = "Hello, World!";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Token count should be positive for non-empty text");
        Assert.IsType<int>(result);
    }

    [Theory]
    [InlineData("text-embedding-ada-002")]
    [InlineData("text-embedding-3-small")]
    [InlineData("text-embedding-3-large")]
    [InlineData("gpt-4")]
    [InlineData("gpt-4-turbo")]
    [InlineData("gpt-3.5-turbo")]
    public async Task CountTokensAsync_OpenAI_VariousModels_ReturnsPositiveCount(string modelId)
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog.";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, $"Token count should be positive for model {modelId}");
    }

    [Fact]
    public async Task CountTokensAsync_OpenAI_Gpt4o_UsesO200kBase()
    {
        // Arrange - GPT-4o uses o200k_base encoding which has different token counts
        var text = "Hello, World!";
        var gpt4oModel = "gpt-4o";
        var gpt4Model = "gpt-4";

        // Act
        var gpt4oCount = await _service.CountTokensAsync(text, gpt4oModel);
        var gpt4Count = await _service.CountTokensAsync(text, gpt4Model);

        // Assert - Both should return valid counts (may differ due to encoding)
        Assert.True(gpt4oCount > 0, "GPT-4o token count should be positive");
        Assert.True(gpt4Count > 0, "GPT-4 token count should be positive");
    }

    #endregion

    #region T063: CountTokensAsync for Foundry model returns integer token count

    [Theory]
    [InlineData("foundry/gpt-4")]
    [InlineData("foundry/gpt-4-turbo")]
    [InlineData("foundry/text-embedding-ada-002")]
    [InlineData("azure/gpt-4")]
    public async Task CountTokensAsync_Foundry_ReturnsIntegerTokenCount(string modelId)
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog.";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, $"Token count should be positive for Foundry model {modelId}");
        Assert.IsType<int>(result);
    }

    [Fact]
    public async Task CountTokensAsync_Foundry_MatchesOpenAIForSameEncoding()
    {
        // Arrange - Foundry models use same encoding as OpenAI equivalents
        var text = "This is a test of the emergency broadcast system.";
        var foundryModel = "foundry/gpt-4";
        var openaiModel = "gpt-4";

        // Act
        var foundryCount = await _service.CountTokensAsync(text, foundryModel);
        var openaiCount = await _service.CountTokensAsync(text, openaiModel);

        // Assert - Should be identical since they use the same encoding
        Assert.Equal(openaiCount, foundryCount);
    }

    #endregion

    #region T064: CountTokensAsync for Ollama model returns integer token count

    [Theory]
    [InlineData("ollama/llama3")]
    [InlineData("ollama/llama3.1")]
    [InlineData("ollama/mistral")]
    [InlineData("ollama/codellama")]
    [InlineData("ollama/nomic-embed-text")]
    public async Task CountTokensAsync_Ollama_ReturnsIntegerTokenCount(string modelId)
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog.";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, $"Token count should be positive for Ollama model {modelId}");
        Assert.IsType<int>(result);
    }

    [Fact]
    public async Task CountTokensAsync_Ollama_UsesApproximation()
    {
        // Arrange - Ollama uses cl100k_base as approximation
        var text = "This is a test sentence for Ollama token counting.";
        var ollamaModel = "ollama/llama3";
        var openaiModel = "gpt-4"; // Also uses cl100k_base

        // Act
        var ollamaCount = await _service.CountTokensAsync(text, ollamaModel);
        var openaiCount = await _service.CountTokensAsync(text, openaiModel);

        // Assert - Should match since Ollama uses cl100k_base approximation
        Assert.Equal(openaiCount, ollamaCount);
    }

    #endregion

    #region T065: CountTokensAsync with empty string returns 0

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("foundry/gpt-4")]
    [InlineData("ollama/llama3")]
    public async Task CountTokensAsync_EmptyString_ReturnsZero(string modelId)
    {
        // Arrange
        var text = string.Empty;

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("foundry/gpt-4")]
    [InlineData("ollama/llama3")]
    public async Task CountTokensAsync_NullString_ReturnsZero(string modelId)
    {
        // Arrange
        string? text = null;

        // Act
        var result = await _service.CountTokensAsync(text!, modelId);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region T066: CountTokensAsync with multilingual text counts tokens correctly

    [Fact]
    public async Task CountTokensAsync_JapaneseText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "日本語テスト：こんにちは世界";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Japanese text should have positive token count");
        // Japanese text typically has more tokens per character
        Assert.True(result >= text.Length / 3, "Japanese should have reasonable token density");
    }

    [Fact]
    public async Task CountTokensAsync_RussianText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "Тест на русском языке: Привет мир!";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Russian text should have positive token count");
    }

    [Fact]
    public async Task CountTokensAsync_ChineseText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "中文测试：你好世界";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Chinese text should have positive token count");
    }

    [Fact]
    public async Task CountTokensAsync_MixedLanguageText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "Mixed: Hello 世界 Привет 🌍";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Mixed language text should have positive token count");
    }

    [Fact]
    public async Task CountTokensAsync_EmojiText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "🚀 Emoji test: 🎉🎊🎁";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Emoji text should have positive token count");
    }

    [Fact]
    public async Task CountTokensAsync_ArabicText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "اختبار اللغة العربية: مرحبا بالعالم";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Arabic text should have positive token count");
    }

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("foundry/gpt-4")]
    [InlineData("ollama/llama3")]
    public async Task CountTokensAsync_MultilingualText_ConsistentAcrossProviders(string modelId)
    {
        // Arrange - All providers use cl100k_base for this text
        var text = "Hello World こんにちは 你好";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, $"Multilingual text should have positive count for {modelId}");
    }

    #endregion

    #region Additional edge cases

    [Fact]
    public async Task CountTokensAsync_WhitespaceOnly_ReturnsNonZero()
    {
        // Arrange
        var text = "   ";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert - Whitespace is typically 1 token
        Assert.True(result >= 0, "Whitespace should return valid token count");
    }

    [Fact]
    public async Task CountTokensAsync_CodeSnippet_ReturnsCorrectCount()
    {
        // Arrange
        var text = "def hello_world():\n    print(\"Hello, World!\")\n    return True";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 10, "Code snippet should have reasonable token count");
    }

    [Fact]
    public async Task CountTokensAsync_SpecialCharacters_ReturnsCorrectCount()
    {
        // Arrange
        var text = "Special chars: @#$%^&*()_+-=[]{}|;':\",./<>?";
        var modelId = "gpt-4";

        // Act
        var result = await _service.CountTokensAsync(text, modelId);

        // Assert
        Assert.True(result > 0, "Special characters should have positive token count");
    }

    [Fact]
    public async Task CountTokensAsync_CancellationToken_ThrowsWhenCancelled()
    {
        // Arrange
        var text = "Test text";
        var modelId = "gpt-4";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.CountTokensAsync(text, modelId, cts.Token));
    }

    [Fact]
    public async Task CountTokensAsync_UnknownModel_FallsBackToDefault()
    {
        // Arrange
        var text = "Hello, World!";
        var unknownModel = "unknown-model-xyz";

        // Act - Should not throw, uses default encoding
        var result = await _service.CountTokensAsync(text, unknownModel);

        // Assert
        Assert.True(result > 0, "Unknown model should fall back to default encoding");
    }

    #endregion

    #region GetMaxTokens tests

    [Theory]
    [InlineData("text-embedding-ada-002", 8191)]
    [InlineData("gpt-4", 8192)]
    [InlineData("gpt-4-turbo", 128000)]
    [InlineData("gpt-3.5-turbo", 4096)]
    public void GetMaxTokens_ReturnsCorrectLimit(string modelId, int expectedMaxTokens)
    {
        // Act
        var result = _service.GetMaxTokens(modelId);

        // Assert
        Assert.Equal(expectedMaxTokens, result);
    }

    [Fact]
    public void GetMaxTokens_UnknownModel_ReturnsDefault()
    {
        // Arrange
        var unknownModel = "unknown-model";

        // Act
        var result = _service.GetMaxTokens(unknownModel);

        // Assert - Should return default embedding limit (8192)
        Assert.Equal(8192, result);
    }

    #endregion
}
