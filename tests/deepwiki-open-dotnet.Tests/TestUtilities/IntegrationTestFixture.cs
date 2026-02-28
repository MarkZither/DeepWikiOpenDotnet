using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DeepWiki.ApiService.Tests.TestUtilities;

/// <summary>
/// Shared WebApplicationFactory fixture for integration tests.
/// Sets ASPNETCORE_ENVIRONMENT=Testing so appsettings.Testing.json is loaded,
/// which supplies a parseable connection string and disables EF auto-migration
/// without hardcoding anything in test code.
/// </summary>
public class IntegrationTestFixture : WebApplicationFactory<DeepWiki.ApiService.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            RemoveAll<IVectorStore>(services);
            services.AddScoped<IVectorStore, MockVectorStore>();

            RemoveAll<IEmbeddingService>(services);
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

            RemoveAll<IDocumentRepository>(services);
            services.AddScoped<IDocumentRepository, MockDocumentRepository>();

            // Remove any real IModelProvider registrations (Ollama/OpenAI) that may
            // be added by Program.cs when CI env vars configure Generation:Providers.
            // CancellationTests adds its own SlowTestProvider via WithWebHostBuilder.
            RemoveAll<DeepWiki.Rag.Core.Providers.IModelProvider>(services);
        });
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors) services.Remove(d);
    }
}
