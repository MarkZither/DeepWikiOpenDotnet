using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        // ConfigureAppConfiguration MUST be called directly on IHostBuilder here,
        // not nested inside ConfigureServices. The auto-migration code in Program.cs
        // runs right after builder.Build(), so configuration overrides must be in
        // the IHostBuilder pipeline before the host is constructed.
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Syntactically valid (but non-existent) Postgres connection string so
                // NpgsqlDataSourceBuilder can parse it without throwing.
                // No real connection is ever made — IVectorStore and IDocumentRepository
                // are replaced by the mock registrations below.
                ["ConnectionStrings:deepwikidb"] = "Host=localhost;Port=5432;Database=deepwiki_test;Username=test;Password=test",
                // Disable EF auto-migration so the app never tries to connect to the fake DB at startup.
                ["VectorStore:AutoMigrate"] = "false"
            });
        });

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
