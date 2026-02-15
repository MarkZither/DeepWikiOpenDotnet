using System.Linq;
using System.Threading;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Providers;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Tests
{
    public class IModelProviderContractTests
    {
        [Fact]
        public void StreamAsync_Method_HasExpectedSignature()
        {
            var type = typeof(IModelProvider);
            var method = type.GetMethod("StreamAsync");
            method.Should().NotBeNull();

            method!.ReturnType.IsGenericType.Should().BeTrue();
            method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
            var genArg = method.ReturnType.GetGenericArguments()[0];
            genArg.Should().Be(typeof(GenerationDelta));

            var hasCancellation = method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken));
            hasCancellation.Should().BeTrue();
        }

        [Fact]
        public void IsAvailableAsync_Returns_TaskOfBool()
        {
            var type = typeof(IModelProvider);
            var method = type.GetMethod("IsAvailableAsync");
            method.Should().NotBeNull();
            method!.ReturnType.Should().Be(typeof(System.Threading.Tasks.Task<bool>));
        }
    }
}
