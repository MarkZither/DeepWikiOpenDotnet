using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Interfaces;
using DeepWiki.ApiService.Tests.TestUtilities;

namespace DeepWiki.ApiService.Tests.Api;

/// <summary>
/// Test fixture for API integration tests using WebApplicationFactory.
/// Provides a test server with configured dependencies and in-memory services.
/// References the Program class from DeepWiki.ApiService namespace.
/// </summary>
public class ApiTestFixture : WebApplicationFactory<DeepWiki.ApiService.Program>
{
    /// <summary>
    /// Configures the test host with test-specific services and settings.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace production registrations with test doubles.
            // Individual tests can further override by calling WithWebHostBuilder.
            RemoveAll<IVectorStore>(services);
            services.AddScoped<IVectorStore, MockVectorStore>();

            RemoveAll<IEmbeddingService>(services);
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

            RemoveAll<IDocumentRepository>(services);
            services.AddScoped<IDocumentRepository, MockDocumentRepository>();

            // Override configuration for test environment
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Tests can add in-memory configuration here if needed
            });
        });

        return base.CreateHost(builder);
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors) services.Remove(d);
    }

    /// <summary>
    /// Creates an HttpClient configured to communicate with the test server.
    /// </summary>
    public new HttpClient CreateClient()
    {
        return base.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}
