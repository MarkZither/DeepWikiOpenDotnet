using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DeepWiki.ApiService.Tests.TestUtilities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Xunit;

namespace DeepWiki.ApiService.Tests.Integration
{
    public class RateLimitingTests : IClassFixture<IntegrationTestFixture>
    {
        private readonly IntegrationTestFixture _factory;

        public RateLimitingTests(IntegrationTestFixture factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SuccessfulResponse_Includes_RateLimit_Headers()
        {
            var client = _factory.CreateClient();
            var res = await client.PostAsJsonAsync("/api/generation/session", new { owner = "test" }, TestContext.Current.CancellationToken);
            res.Headers.Should().Contain(h => h.Key == "X-RateLimit-Limit" || h.Key == "X-RateLimit-Remaining" || h.Key == "X-RateLimit-Reset");
        }

        [Fact]
        public async Task ExceedingLimit_ShouldReturn_429_WithRateLimitHeaders()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Make many requests in parallel to create a burst that will exceed the 100 req/min limit
            var tasks = Enumerable.Range(0, 200)
                .Select(_ => client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken))
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            // Assert: at least one response should be 429 and include Retry-After header
            responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests);
            responses.Should().Contain(r => r.Headers.Contains("Retry-After"));

            // Additionally, verify X-RateLimit headers are present on responses where applicable
            responses.Should().Contain(r => r.Headers.Contains("X-RateLimit-Limit") || r.Headers.Contains("X-RateLimit-Remaining") || r.Headers.Contains("X-RateLimit-Reset"));
        }
    }
}
