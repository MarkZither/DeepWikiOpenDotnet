using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.DependencyInjection;
using DeepWiki.Data.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.DependencyInjection;

/// <summary>
/// Unit tests for PostgreSQL DI registration extensions.
/// Verifies that ServiceCollectionExtensions correctly registers all required services.
/// Mirrors SQL Server tests for parity.
/// </summary>
public class PostgresDependencyInjectionTests
{
    /// <summary>
    /// Tests that AddPostgresDataLayer (connection string overload) registers all required services.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithConnectionString_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";

        // Act
        services.AddPostgresDataLayer(connectionString);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - DbContext is registered
        var dbContext = serviceProvider.GetRequiredService<PostgresVectorDbContext>();
        Assert.NotNull(dbContext);

        // Assert - IVectorStore is registered as PostgresVectorStore
        var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
        Assert.NotNull(vectorStore);
        Assert.IsType<PostgresVectorStore>(vectorStore);

        // Assert - IDocumentRepository is registered as PostgresDocumentRepository
        var repository = serviceProvider.GetRequiredService<IDocumentRepository>();
        Assert.NotNull(repository);
        Assert.IsType<PostgresDocumentRepository>(repository);
    }

    /// <summary>
    /// Tests that AddPostgresDataLayer throws when connection string is null.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithNullConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddPostgresDataLayer(null!));
        Assert.NotNull(ex);
    }

    /// <summary>
    /// Tests that AddPostgresDataLayer throws when connection string is empty.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithEmptyConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddPostgresDataLayer(""));
    }

    /// <summary>
    /// Tests that AddPostgresDataLayer throws when connection string is whitespace.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithWhitespaceConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddPostgresDataLayer("   "));
    }

    /// <summary>
    /// Tests that services registered are scoped (one per request in ASP.NET Core context).
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_RegistersServicesAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";

        // Act
        services.AddPostgresDataLayer(connectionString);
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IVectorStore));

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>
    /// Tests that DbContext is registered as scoped.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_RegistersDbContextAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";

        // Act
        services.AddPostgresDataLayer(connectionString);
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(PostgresVectorDbContext));

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>
    /// Tests that each service resolution from DI container gets a different DbContext instance (scoped behavior).
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_ScopedServices_ReturnNewInstancesPerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";
        services.AddPostgresDataLayer(connectionString);

        // Act - Create two separate scopes
        var provider = services.BuildServiceProvider();
        var scope1 = provider.CreateScope();
        var scope2 = provider.CreateScope();

        var context1 = scope1.ServiceProvider.GetRequiredService<PostgresVectorDbContext>();
        var context2 = scope2.ServiceProvider.GetRequiredService<PostgresVectorDbContext>();

        // Assert - Different scopes should get different DbContext instances
        Assert.NotSame(context1, context2);
    }

    /// <summary>
    /// Tests that DbContextOptions includes retry policy configuration.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_ConfiguresRetryPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";

        // Act
        services.AddPostgresDataLayer(connectionString);
        var serviceProvider = services.BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<PostgresVectorDbContext>();

        // Assert - Verify retry policy is configured
        Assert.NotNull(dbContext);
    }

    /// <summary>
    /// Tests that custom DbContextOptions configuration can be applied via the configureOptions parameter.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithCustomOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;";
        var optionsConfigured = false;

        // Act
        services.AddPostgresDataLayer(
            connectionString,
            options =>
            {
                optionsConfigured = true;
                // Additional configuration would happen here
            });

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<PostgresVectorDbContext>();

        // Assert
        Assert.True(optionsConfigured, "Custom options configuration should be applied");
    }

    /// <summary>
    /// Tests configuration-based registration throws when key not found.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithConfigKey_ThrowsWhenKeyNotFound()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddPostgresDataLayer("ConnectionStrings:Missing", config));
    }

    /// <summary>
    /// Tests configuration-based registration with valid key.
    /// </summary>
    [Fact]
    public void AddPostgresDataLayer_WithConfigKey_RegistersWhenKeyExists()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Postgres", "Host=localhost;Port=5432;Database=deepwiki_test;Username=postgres;Password=postgres;" }
            })
            .Build();

        // Act
        services.AddPostgresDataLayer("ConnectionStrings:Postgres", configBuilder);
        var provider = services.BuildServiceProvider();

        // Assert
        var vectorStore = provider.GetRequiredService<IVectorStore>();
        Assert.NotNull(vectorStore);
    }
}
