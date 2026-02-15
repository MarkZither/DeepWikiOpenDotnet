using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DeepWiki.ApiService.Tests.Integration;

public class PrometheusExportTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PrometheusExportTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var items = new[] { new KeyValuePair<string, string?>("OpenTelemetry:Prometheus:Enabled", "true") };
                cfg.AddInMemoryCollection(items);
            });
        });
    }

    [Fact]
    public async Task Metrics_endpoint_returns_generation_metrics()
    {
        var client = _factory.CreateClient();

        // Prime metrics by calling the GenerationMetrics instance and recording some values
        var sp = _factory.Services;
        var gm = sp.GetService(typeof(DeepWiki.Rag.Core.Observability.GenerationMetrics)) as DeepWiki.Rag.Core.Observability.GenerationMetrics;
        gm.Should().NotBeNull();
        gm!.RecordTokens(5, "test");
        gm.RecordError("timeout", "test");
        gm.RecordTimeToFirstToken(123.4, "test");

        var res = await client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var txt = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        txt.Should().Contain("generation_tokens_total");
        txt.Should().Contain("generation_errors_total");
        txt.Should().Contain("generation_ttf_last_ms");
    }
}
