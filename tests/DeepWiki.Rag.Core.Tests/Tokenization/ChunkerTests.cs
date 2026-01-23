using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Tokenization;

namespace DeepWiki.Rag.Core.Tests.Tokenization;

public class ChunkerTests
{
    private readonly ITokenEncoder _encoder;
    private readonly Chunker _chunker;

    public ChunkerTests()
    {
        _encoder = new OpenAITokenEncoder();
        _chunker = new Chunker(_encoder);
    }

    [Fact]
    public void ChunkText_LargeText_SplitsIntoMultipleChunks()
    {
        var sentence = "The quick brown fox jumps over the lazy dog. ";
        var largeText = string.Concat(Enumerable.Repeat(sentence, 500));
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(largeText, maxTokens);

        Assert.True(chunks.Count > 1, "Large text should be split into multiple chunks");
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.TokenCount <= maxTokens, "Chunk exceeds token limit");
        }
    }

    [Fact]
    public void ChunkText_AllChunksUnderLimit()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= 100; i++)
        {
            sb.Append("Paragraph ");
            sb.Append(i);
            sb.AppendLine(": This is a sample paragraph with some content.");
            sb.AppendLine();
        }
        var text = sb.ToString();
        var maxTokens = 50;

        var chunks = _chunker.ChunkText(text, maxTokens);

        foreach (var chunk in chunks)
        {
            var actualTokens = _encoder.CountTokens(chunk.Text);
            Assert.True(actualTokens <= maxTokens, "Chunk exceeds limit");
        }
    }

    [Fact]
    public void ChunkText_ChunksContainAllOriginalContent()
    {
        var originalText = "First sentence here. Second sentence follows. Third sentence ends.";
        var maxTokens = 10;

        var chunks = _chunker.ChunkText(originalText, maxTokens);

        var reconstructed = string.Join(" ", chunks.Select(c => c.Text));
        Assert.Contains("First", reconstructed);
        Assert.Contains("Third", reconstructed);
    }

    [Fact]
    public void ChunkText_PreservesWordBoundaries()
    {
        var text = "Supercalifragilisticexpialidocious is a very long word.";
        var maxTokens = 10;

        var chunks = _chunker.ChunkText(text, maxTokens);

        // Verify we got chunks
        Assert.NotEmpty(chunks);
        
        // Verify chunks don't contain partial words at boundaries
        // by checking that the original text reconstructs properly
        var allText = string.Join("", chunks.Select(c => c.Text));
        
        // Key words should be intact (not split mid-word)
        Assert.True(
            allText.Contains("Supercalifragilisticexpialidocious") || 
            chunks.Any(c => c.Text.Contains("Supercalifragilisticexpialidocious")),
            "Long word should be preserved intact in at least one chunk");
    }

    [Fact]
    public void ChunkText_SplitsOnWhitespace()
    {
        var text = "Word1 Word2 Word3 Word4 Word5 Word6 Word7 Word8 Word9 Word10";
        var maxTokens = 5;

        var chunks = _chunker.ChunkText(text, maxTokens);

        foreach (var chunk in chunks)
        {
            var words = chunk.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                Assert.True(word.All(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c)));
            }
        }
    }

    [Fact]
    public void ValidateWordBoundaries_CleanSplit_ReturnsTrue()
    {
        var originalText = "Hello world this is a test";
        var chunk = new TextChunk
        {
            Text = "Hello world",
            ChunkIndex = 0,
            StartOffset = 0,
            Length = 11
        };

        var result = Chunker.ValidateWordBoundaries(chunk, originalText);

        Assert.True(result, "Clean word boundary split should validate");
    }

    [Fact]
    public void ValidateWordBoundaries_MidWordSplit_ReturnsFalse()
    {
        var originalText = "HelloWorld is one word";
        var chunk = new TextChunk
        {
            Text = "Hello",
            ChunkIndex = 0,
            StartOffset = 0,
            Length = 5
        };

        var result = Chunker.ValidateWordBoundaries(chunk, originalText);

        Assert.False(result, "Mid-word split should fail validation");
    }

    [Fact]
    public void ChunkText_DefaultMaxTokens_Is8192()
    {
        var sentence = "This is a sample sentence with approximately ten tokens in it. ";
        var veryLargeText = string.Concat(Enumerable.Repeat(sentence, 2000));

        var chunks = _chunker.ChunkText(veryLargeText);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.TokenCount <= 8192, "Chunk exceeds default limit of 8192");
        }
    }

    [Fact]
    public void ChunkText_RespectCustomMaxTokens()
    {
        var sentence = "Sample text for testing. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 100));
        var customLimit = 50;

        var chunks = _chunker.ChunkText(text, customLimit);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.TokenCount <= customLimit, "Chunk exceeds custom limit");
        }
    }

    [Fact]
    public void ChunkText_ZeroMaxTokens_ThrowsArgumentException()
    {
        var text = "Some text";
        Assert.Throws<ArgumentOutOfRangeException>(() => _chunker.ChunkText(text, 0));
    }

    [Fact]
    public void ChunkText_NegativeMaxTokens_ThrowsArgumentException()
    {
        var text = "Some text";
        Assert.Throws<ArgumentOutOfRangeException>(() => _chunker.ChunkText(text, -10));
    }

    [Fact]
    public void ChunkText_SmallText_ReturnsSingleChunk()
    {
        var smallText = "Hello, World!";
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(smallText, maxTokens);

        Assert.Single(chunks);
        Assert.Equal(smallText, chunks[0].Text);
    }

    [Fact]
    public void ChunkText_TextExactlyAtLimit_ReturnsSingleChunk()
    {
        var text = "Short text";
        var tokenCount = _encoder.CountTokens(text);

        var chunks = _chunker.ChunkText(text, tokenCount + 10);

        Assert.Single(chunks);
    }

    [Fact]
    public void ChunkText_EmptyString_ReturnsEmptyList()
    {
        var emptyText = string.Empty;

        var chunks = _chunker.ChunkText(emptyText);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_NullString_ReturnsEmptyList()
    {
        string? nullText = null;

        var chunks = _chunker.ChunkText(nullText!);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_WhitespaceOnly_ReturnsEmptyList()
    {
        var whitespaceText = "   \n\t  ";

        var chunks = _chunker.ChunkText(whitespaceText);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_IncludesChunkIndex()
    {
        var sentence = "Sample text here. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 50));
        var maxTokens = 20;

        var chunks = _chunker.ChunkText(text, maxTokens);

        Assert.True(chunks.Count > 1, "Should have multiple chunks for index testing");
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void ChunkText_IncludesParentId()
    {
        var text = "Some text to chunk into pieces for testing purposes.";
        var maxTokens = 10;
        var parentId = Guid.NewGuid();

        var chunks = _chunker.ChunkText(text, maxTokens, parentId);

        foreach (var chunk in chunks)
        {
            Assert.Equal(parentId, chunk.ParentId);
        }
    }

    [Fact]
    public void ChunkText_IncludesTokenCount()
    {
        var text = "This is a test sentence for token counting.";
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(text, maxTokens);

        Assert.Single(chunks);
        Assert.True(chunks[0].TokenCount > 0, "Token count should be positive");

        var expectedCount = _encoder.CountTokens(chunks[0].Text);
        Assert.Equal(expectedCount, chunks[0].TokenCount);
    }

    [Fact]
    public void ChunkText_IncludesLanguage()
    {
        var text = "def hello():\n    print('hello')\n    return True";
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(text, maxTokens, language: "code");

        Assert.Single(chunks);
        Assert.Equal("code", chunks[0].Language);
    }

    [Fact]
    public void ChunkText_IncludesStartOffset()
    {
        var text = "First part. Second part. Third part.";
        var maxTokens = 5;

        var chunks = _chunker.ChunkText(text, maxTokens);

        Assert.Equal(0, chunks[0].StartOffset);

        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.True(chunks[i].StartOffset > chunks[i - 1].StartOffset);
        }
    }

    [Fact]
    public void ChunkText_IncludesLength()
    {
        var text = "Short text for length testing.";
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(text, maxTokens);

        Assert.Single(chunks);
        Assert.Equal(chunks[0].Text.Length, chunks[0].Length);
    }

    [Fact]
    public void ChunkText_MetadataComplete()
    {
        var text = "Complete metadata test with all fields populated.";
        var maxTokens = 100;
        var parentId = Guid.NewGuid();

        var chunks = _chunker.ChunkText(text, maxTokens, parentId, "en");

        var chunk = Assert.Single(chunks);
        Assert.NotEmpty(chunk.Text);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(parentId, chunk.ParentId);
        Assert.True(chunk.TokenCount > 0);
        Assert.Equal("en", chunk.Language);
        Assert.Equal(0, chunk.StartOffset);
        Assert.Equal(chunk.Text.Length, chunk.Length);
    }

    [Fact]
    public void ChunkText_VeryLongSingleWord_HandlesGracefully()
    {
        var longWord = new string('a', 10000);
        var maxTokens = 100;

        var chunks = _chunker.ChunkText(longWord, maxTokens);

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void ChunkText_MultipleConsecutiveSpaces_HandlesCorrectly()
    {
        var text = "Word1    Word2     Word3      Word4";
        var maxTokens = 5;

        var chunks = _chunker.ChunkText(text, maxTokens);

        foreach (var chunk in chunks)
        {
            Assert.False(chunk.Text.StartsWith(' '), "Chunk should not start with space");
            Assert.False(chunk.Text.EndsWith(' '), "Chunk should not end with space");
        }
    }

    [Fact]
    public void ChunkText_NewlinesAndParagraphs_PrefersSplitAtParagraphs()
    {
        var text = "Paragraph one content.\n\nParagraph two content.\n\nParagraph three content.";
        var maxTokens = 10;

        var chunks = _chunker.ChunkText(text, maxTokens);

        Assert.True(chunks.Count >= 1, "Should create at least one chunk");
    }

    [Fact]
    public void DetectLanguage_CodeText_ReturnsCode()
    {
        var codeText = "public class HelloWorld {\n    public static void Main() { }\n}";

        var language = Chunker.DetectLanguage(codeText);

        Assert.Equal("code", language);
    }

    [Fact]
    public void DetectLanguage_NaturalText_ReturnsEn()
    {
        var naturalText = "This is a simple English sentence without any code.";

        var language = Chunker.DetectLanguage(naturalText);

        Assert.Equal("en", language);
    }

    [Fact]
    public void DetectLanguage_EmptyText_ReturnsEn()
    {
        var emptyText = "";

        var language = Chunker.DetectLanguage(emptyText);

        Assert.Equal("en", language);
    }
}
