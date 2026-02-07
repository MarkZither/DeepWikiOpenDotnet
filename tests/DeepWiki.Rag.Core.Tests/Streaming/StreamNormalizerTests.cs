using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Streaming;

namespace DeepWiki.Rag.Core.Tests.Streaming
{
    public class StreamNormalizerTests
    {
        [Fact]
        public void SequenceAssignment_ShouldAssignMonotonicSeqStartingAtZero()
        {
            var normalizer = new StreamNormalizer("prompt-1", "assistant");
            var chunks = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("Hello"),
                Encoding.UTF8.GetBytes(" "),
                Encoding.UTF8.GetBytes("world")
            };

            var deltas = normalizer.Normalize(chunks).ToList();

            deltas.Select(d => d.Seq).Should().BeInAscendingOrder();
            deltas.Select(d => d.Seq).Should().Equal(new[] { 0, 1, 2 });
            deltas.Select(d => d.Text).Should().Equal(new[] { "Hello", " ", "world" });
            deltas.Should().OnlyContain(d => d.Type == "token" && d.Role == "assistant" && d.PromptId == "prompt-1");
        }

        [Fact]
        public void Deduplication_ShouldCollapseConsecutiveDuplicates()
        {
            var normalizer = new StreamNormalizer("p2", "assistant");
            var chunks = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("a"),
                Encoding.UTF8.GetBytes("a"),
                Encoding.UTF8.GetBytes("b"),
                Encoding.UTF8.GetBytes("b"),
                Encoding.UTF8.GetBytes("c")
            };

            var deltas = normalizer.Normalize(chunks).ToList();

            deltas.Select(d => d.Text).Should().Equal(new[] { "a", "b", "c" });
            deltas.Select(d => d.Seq).Should().Equal(new[] { 0, 1, 2 });
        }

        [Fact]
        public void Utf8IncompleteByteHandling_ShouldConcatSplitMultiByteCharacter()
        {
            // Use an emoji which is multi-byte in UTF-8
            var emoji = "😊"; // multi-byte
            var emojiBytes = Encoding.UTF8.GetBytes(emoji);

            // Split the emoji bytes across two chunks
            var chunk1 = new byte[] { (byte)'H' };
            var firstHalf = emojiBytes.Take(emojiBytes.Length / 2).ToArray();
            var secondHalf = emojiBytes.Skip(emojiBytes.Length / 2).ToArray();

            var normalizer = new StreamNormalizer("p3", "assistant");
            var chunks = new List<byte[]>
            {
                chunk1.Concat(firstHalf).ToArray(),
                secondHalf.Concat(Encoding.UTF8.GetBytes("i")).ToArray()
            };

            var deltas = normalizer.Normalize(chunks).ToList();

            deltas.Select(d => d.Text).Should().Equal(new[] { "H", emoji + "i" });
            deltas.Should().HaveCount(2);
            deltas.First().Seq.Should().Be(0);
            deltas.Last().Seq.Should().Be(1);
        }

        [Fact]
        public void EmptyInput_ShouldReturnEmptyCollection()
        {
            var normalizer = new StreamNormalizer("p4", "assistant");
            var chunks = new List<byte[]>();

            var deltas = normalizer.Normalize(chunks);

            deltas.Should().BeEmpty();
        }
    }
}
