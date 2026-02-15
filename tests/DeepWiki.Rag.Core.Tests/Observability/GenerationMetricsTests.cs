using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Observability;

namespace DeepWiki.Rag.Core.Tests.Observability
{
    public class GenerationMetricsTests
    {
        [Fact]
        public void PublicMethods_Exist_With_Expected_Signatures()
        {
            var type = typeof(GenerationMetrics);
            var hasStart = type.GetMethod("StartTtfMeasurement");
            hasStart.Should().NotBeNull();
            hasStart!.ReturnType.Should().Be(typeof(Stopwatch));

            var rtf = type.GetMethod("RecordTimeToFirstToken");
            rtf.Should().NotBeNull();
            var rtfParams = rtf!.GetParameters();
            rtfParams.Select(p => p.ParameterType).Should().Contain(new[] { typeof(double), typeof(string) });

            var recTokens = type.GetMethod("RecordTokens");
            recTokens.Should().NotBeNull();
            recTokens!.GetParameters().Select(p => p.ParameterType).Should().Contain(new[] { typeof(long), typeof(string) });

            var recErr = type.GetMethod("RecordError");
            recErr.Should().NotBeNull();
            recErr!.GetParameters().Select(p => p.ParameterType).Should().Contain(new[] { typeof(string), typeof(string) });
        }
    }
}
