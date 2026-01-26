using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.VectorStore;

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
            // Remove production IVectorStore registration and replace with NoOp for testing
            var vectorStoreDescriptor = services.FirstOrDefault(d => 
                d.ServiceType == typeof(IVectorStore));
            if (vectorStoreDescriptor != null)
            {
                services.Remove(vectorStoreDescriptor);
            }
            
            // Register NoOpVectorStore for tests (individual tests can override with mocks)
            services.AddScoped<IVectorStore, NoOpVectorStore>();
            
            // Override configuration for test environment
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Tests can add in-memory configuration here if needed
            });
        });

        return base.CreateHost(builder);
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
