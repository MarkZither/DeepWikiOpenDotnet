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

            // Make 101 requests in quick succession to exceed the 100 req/min limit
            HttpResponseMessage lastResponse = null!;
            for (int i = 0; i < 101; i++)
            {
                var resp = await client.GetAsync("/weatherforecast");
                lastResponse = resp;
            }

            // Assert: last response should be 429 and include Retry-After header
            lastResponse.Should().NotBeNull();
            lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            lastResponse.Headers.Should().ContainKey("Retry-After");
        }
    }
}
