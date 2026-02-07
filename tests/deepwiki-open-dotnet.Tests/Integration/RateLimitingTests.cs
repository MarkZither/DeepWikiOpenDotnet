using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DeepWiki.ApiService.Tests.Integration
{
    public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RateLimitingTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
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
        }
    }
}
