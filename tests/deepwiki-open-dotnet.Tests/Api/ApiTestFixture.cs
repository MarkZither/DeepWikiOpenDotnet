using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Interfaces;
using DeepWiki.ApiService.Tests.TestUtilities;

namespace DeepWiki.ApiService.Tests.Api;

/// <summary>
/// Test fixture for API integration tests using WebApplicationFactory.
/// Sets ASPNETCORE_ENVIRONMENT=Testing so appsettings.Testing.json is loaded,
/// which supplies a parseable connection string and disables EF auto-migration
/// without hardcoding anything in test code.
/// </summary>
public class ApiTestFixture : WebApplicationFactory<DeepWiki.ApiService.Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // Load appsettings.Testing.json — this is evaluated before Program.cs
        // registers services, so the connection string check and AutoMigrate flag
        // are both satisfied before any code in Program.Main runs.
        builder.UseEnvironment("Testing");

        // Replace all external-dependency services with in-memory test doubles.
        builder.ConfigureServices(services =>
        {
            RemoveAll<IVectorStore>(services);
            services.AddScoped<IVectorStore, MockVectorStore>();

            RemoveAll<IEmbeddingService>(services);
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

            RemoveAll<IDocumentRepository>(services);
            services.AddScoped<IDocumentRepository, MockDocumentRepository>();
        });
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
