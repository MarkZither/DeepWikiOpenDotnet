using System;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Models;

namespace DeepWiki.Rag.Core.Tests.Models
{
    public class PromptTests
    {
        [Fact]
        public void Prompt_DefaultValues_AreCorrect()
        {
            var prompt = new Prompt
            {
                PromptId = Guid.NewGuid().ToString(),
                SessionId = "s-1",
                Text = "hello",
                IdempotencyKey = "k1",
                CreatedAt = DateTime.UtcNow
            };

            prompt.TokenCount.Should().Be(0);
            prompt.Status.Should().Be(PromptStatus.InFlight);
            prompt.IdempotencyKey.Should().Be("k1");
        }
    }
}
