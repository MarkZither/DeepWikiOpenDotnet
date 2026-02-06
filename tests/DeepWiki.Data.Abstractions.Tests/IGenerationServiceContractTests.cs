using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Data.Abstractions.Tests
{
    public class IGenerationServiceContractTests
    {
        [Fact]
        public void GenerateAsync_Method_HasExpectedSignature()
        {
            var type = typeof(IGenerationService);
            var method = type.GetMethod("GenerateAsync");

            method.Should().NotBeNull();

            // Return type should be IAsyncEnumerable<GenerationDelta>
            method!.ReturnType.IsGenericType.Should().BeTrue();
            method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
            var genArg = method.ReturnType.GetGenericArguments()[0];
            genArg.Should().Be(typeof(GenerationDelta));

            // Ensure a CancellationToken parameter exists
            var hasCancellation = method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken));
            hasCancellation.Should().BeTrue();
        }
    }
}
