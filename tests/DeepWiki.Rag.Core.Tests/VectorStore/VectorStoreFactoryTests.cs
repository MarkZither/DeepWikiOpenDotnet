using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.VectorStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.VectorStore;

/// <summary>
/// Unit tests for VectorStoreFactory provider selection and fallback logic.
/// </summary>
public class VectorStoreFactoryTests
{
    [Fact]
    public void GetConfiguredProvider_DefaultsToSqlServer()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act
        var provider = factory.GetConfiguredProvider();

        // Assert
        Assert.Equal("sqlserver", provider);
    }

    [Fact]
    public void GetConfiguredProvider_ReturnsConfiguredProvider()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "VectorStore:Provider", "postgres" }
            })
            .Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act
        var provider = factory.GetConfiguredProvider();

        // Assert
        Assert.Equal("postgres", provider);
    }

    [Fact]
    public void IsProviderAvailable_SqlServer_ReturnsTrueWhenConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "VectorStore:SqlServer:ConnectionString", "Server=localhost;Database=test;" }
            })
            .Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act
        var available = factory.IsProviderAvailable("sqlserver");

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsProviderAvailable_Postgres_ReturnsTrueWhenConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "VectorStore:Postgres:ConnectionString", "Host=localhost;Database=test;" }
            })
            .Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act
        var available = factory.IsProviderAvailable("postgres");

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsProviderAvailable_ReturnsFalseWhenNotConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act
        var sqlServerAvailable = factory.IsProviderAvailable("sqlserver");
        var postgresAvailable = factory.IsProviderAvailable("postgres");

        // Assert
        Assert.False(sqlServerAvailable);
        Assert.False(postgresAvailable);
    }

    [Fact]
    public void Create_FallsBackToNoOpWhenProviderNotAvailable()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "VectorStore:Provider", "sqlserver" }
                // No connection string configured
            })
            .Build();
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration, serviceProvider.GetService<ILoggerFactory>());

        // Act
        var store = factory.Create();

        // Assert
        Assert.NotNull(store);
        Assert.IsType<NoOpVectorStore>(store);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    [InlineData("postgresql")]
    [InlineData("pgvector")]
    public void CreateForProvider_SupportsKnownProviders(string provider)
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration, serviceProvider.GetService<ILoggerFactory>());

        // Act - should not throw for known providers (will return NoOp since not registered)
        var store = factory.CreateForProvider(provider);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void CreateForProvider_ThrowsForUnknownProvider()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new VectorStoreFactory(serviceProvider, configuration);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => factory.CreateForProvider("unknown"));
        Assert.Contains("Unknown vector store provider", exception.Message);
    }
}
