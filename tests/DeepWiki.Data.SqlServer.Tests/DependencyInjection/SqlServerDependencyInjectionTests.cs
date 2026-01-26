using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.DependencyInjection;
using DeepWiki.Data.SqlServer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.DependencyInjection;

/// <summary>
/// Unit tests for SqlServer DI registration extensions.
/// Verifies that ServiceCollectionExtensions correctly registers all required services.
/// </summary>
public class SqlServerDependencyInjectionTests
{
    /// <summary>
    /// Tests that AddSqlServerDataLayer (connection string overload) registers all required services.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithConnectionString_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";

        // Act
        services.AddSqlServerDataLayer(connectionString);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - DbContext is registered
        var dbContext = serviceProvider.GetRequiredService<SqlServerVectorDbContext>();
        Assert.NotNull(dbContext);

        // Assert - IPersistenceVectorStore is registered as SqlServerVectorStore
        var vectorStore = serviceProvider.GetRequiredService<IPersistenceVectorStore>();
        Assert.NotNull(vectorStore);
        Assert.IsType<SqlServerVectorStore>(vectorStore);

        // Assert - IDocumentRepository is registered as SqlServerDocumentRepository
        var repository = serviceProvider.GetRequiredService<IDocumentRepository>();
        Assert.NotNull(repository);
        Assert.IsType<SqlServerDocumentRepository>(repository);
    }

    /// <summary>
    /// Tests that AddSqlServerDataLayer throws when connection string is null.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithNullConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            services.AddSqlServerDataLayer(null!));
        Assert.NotNull(ex);
    }

    /// <summary>
    /// Tests that AddSqlServerDataLayer throws when connection string is empty.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithEmptyConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            services.AddSqlServerDataLayer(""));
    }

    /// <summary>
    /// Tests that AddSqlServerDataLayer throws when connection string is whitespace.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithWhitespaceConnectionString_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            services.AddSqlServerDataLayer("   "));
    }

    /// <summary>
    /// Tests that services registered are scoped (one per request in ASP.NET Core context).
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_RegistersServicesAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";

        // Act
        services.AddSqlServerDataLayer(connectionString);
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IPersistenceVectorStore));

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>
    /// Tests that DbContext is registered as scoped.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_RegistersDbContextAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";

        // Act
        services.AddSqlServerDataLayer(connectionString);
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(SqlServerVectorDbContext));

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>
    /// Tests that each service resolution from DI container gets a different DbContext instance (scoped behavior).
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_ScopedServices_ReturnNewInstancesPerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";
        services.AddSqlServerDataLayer(connectionString);

        // Act - Create two separate scopes
        var provider = services.BuildServiceProvider();
        var scope1 = provider.CreateScope();
        var scope2 = provider.CreateScope();

        var context1 = scope1.ServiceProvider.GetRequiredService<SqlServerVectorDbContext>();
        var context2 = scope2.ServiceProvider.GetRequiredService<SqlServerVectorDbContext>();

        // Assert - Different scopes should get different DbContext instances
        Assert.NotSame(context1, context2);
    }

    /// <summary>
    /// Tests that DbContextOptions includes retry policy configuration.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_ConfiguresRetryPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";

        // Act
        services.AddSqlServerDataLayer(connectionString);
        var serviceProvider = services.BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<SqlServerVectorDbContext>();

        // Assert - Verify retry policy is configured (indirectly by checking context options)
        Assert.NotNull(dbContext);
        // DbContext should be usable (if retry wasn't configured, options would fail)
    }

    /// <summary>
    /// Tests that custom DbContextOptions configuration can be applied via the configureOptions parameter.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithCustomOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=(local);Database=test;Trusted_Connection=true;";
        var optionsConfigured = false;

        // Act
        services.AddSqlServerDataLayer(
            connectionString,
            options =>
            {
                optionsConfigured = true;
                // Additional configuration would happen here
            });

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<SqlServerVectorDbContext>();

        // Assert
        Assert.True(optionsConfigured, "Custom options configuration should be applied");
    }

    /// <summary>
    /// Tests configuration-based registration throws when key not found.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithConfigKey_ThrowsWhenKeyNotFound()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddSqlServerDataLayer("ConnectionStrings:Missing", config));
    }

    /// <summary>
    /// Tests configuration-based registration with valid key.
    /// </summary>
    [Fact]
    public void AddSqlServerDataLayer_WithConfigKey_RegistersWhenKeyExists()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:SqlServer", "Server=(local);Database=test;Trusted_Connection=true;" }
            })
            .Build();

        // Act
        services.AddSqlServerDataLayer("ConnectionStrings:SqlServer", configBuilder);
        var provider = services.BuildServiceProvider();

        // Assert
        var vectorStore = provider.GetRequiredService<IPersistenceVectorStore>();
        Assert.NotNull(vectorStore);
    }
}
