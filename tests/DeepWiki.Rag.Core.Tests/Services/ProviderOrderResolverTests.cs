using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Xunit;
using DeepWiki.Rag.Core.Providers;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Tests.Services
{
    public class ProviderOrderResolverTests
    {
        private class DummyProvider : IModelProvider
        {
            public DummyProvider(string name) { Name = name; }
            public string Name { get; }
            public System.Threading.Tasks.Task<bool> IsAvailableAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(true);
            public async IAsyncEnumerable<GenerationDelta> StreamAsync(string prompt, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                yield break;
            }
        }

        [Fact]
        public void ConfiguredOrder_IsRespected()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Generation:Providers:0"] = "OpenAI",
                    ["Generation:Providers:1"] = "Ollama"
                })
                .Build();

            var providers = new List<IModelProvider> { new DummyProvider("Ollama"), new DummyProvider("OpenAI") };

            var ordered = ProviderOrderResolver.ResolveOrder(providers, config);

            Assert.Equal(2, ordered.Count);
            Assert.Equal("OpenAI", ordered[0].Name);
            Assert.Equal("Ollama", ordered[1].Name);
        }

        [Fact]
        public void MissingConfig_FallsBackToRegistrationOrder()
        {
            var config = new ConfigurationBuilder().Build();
            var providers = new List<IModelProvider> { new DummyProvider("Ollama"), new DummyProvider("OpenAI") };

            var ordered = ProviderOrderResolver.ResolveOrder(providers, config);

            Assert.Equal(2, ordered.Count);
            Assert.Equal("Ollama", ordered[0].Name);
            Assert.Equal("OpenAI", ordered[1].Name);
        }
    }
}
