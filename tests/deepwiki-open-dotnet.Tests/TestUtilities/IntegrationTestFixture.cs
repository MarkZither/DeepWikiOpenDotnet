using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DeepWiki.ApiService.Tests.TestUtilities;

/// <summary>
/// Shared WebApplicationFactory fixture for integration tests.
/// Provides the application with a parseable (but non-existent) Postgres connection
/// string, disables EF auto-migration, and replaces all external-dependency services
/// with in-memory test doubles so the app can start without any real infrastructure.
/// </summary>
public class IntegrationTestFixture : WebApplicationFactory<DeepWiki.ApiService.Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // ConfigureAppConfiguration MUST be called here, directly on IHostBuilder,
        // before ConfigureServices. Program.cs runs EF auto-migration right after
        // builder.Build(), so config overrides must be in the pipeline first.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Syntactically valid (but non-existent) connection string so
                // NpgsqlDataSourceBuilder can parse it without throwing.
                ["ConnectionStrings:deepwikidb"] = "Host=localhost;Port=5432;Database=deepwiki_test;Username=test;Password=test",
                // Disable EF auto-migration so the app never tries to connect to the fake DB.
                ["VectorStore:AutoMigrate"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
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
}
