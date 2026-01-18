using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Console application demonstrating DeepWiki data access layer setup with dependency injection.
/// 
/// This example shows how to:
/// 1. Load configuration from User Secrets and environment variables
/// 2. Register SQL Server data layer services securely
/// 3. Add documents to the database
/// 4. Query documents by similarity
/// 5. Switch between SQL Server and PostgreSQL via configuration
/// 
/// To run this example:
/// 1. Set up User Secrets: dotnet user-secrets init
/// 2. Add connection string: dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your-connection-string>"
/// 3. Run: dotnet run
/// </summary>
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        // Load configuration from User Secrets (dev), environment variables, and appsettings
        config.AddUserSecrets<Program>();
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Get configuration values from secure sources only
        var databaseType = context.Configuration["DatabaseType"] ?? "SqlServer";
        var connectionString = context.Configuration["ConnectionStrings:DefaultConnection"];
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string not found. Configure via User Secrets, environment variables, or appsettings.json");
        }

        if (databaseType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // Register SQL Server data layer - connection string comes from secure configuration
            services.AddSqlServerDataLayer("ConnectionStrings:DefaultConnection", context.Configuration);
            Console.WriteLine("Configured for SQL Server");
        }
        else if (databaseType.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Register PostgreSQL data layer
            services.AddPostgresDataLayer("ConnectionStrings:DefaultConnection", context.Configuration);
            Console.WriteLine("Configured for PostgreSQL");
        }
        else
        {
            throw new InvalidOperationException($"Unknown database type: {databaseType}");
        }
    })
    .Build();

// Get the vector store from DI container
var vectorStore = host.Services.GetRequiredService<IVectorStore>();
var documentRepository = host.Services.GetRequiredService<IDocumentRepository>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("DeepWiki Data Access Layer - DI Example Starting");

// Example 1: Add a document
logger.LogInformation("=== Example 1: Adding a document ===");
var newDocument = new DocumentEntity
{
    Id = Guid.NewGuid(),
    RepoUrl = "https://github.com/example/deepwiki",
    FilePath = "src/Program.cs",
    Title = "Main Program File",
    Text = "This is sample content from a C# source file",
    Embedding = GenerateSampleEmbedding(),
    FileType = "csharp",
    IsCode = true,
    IsImplementation = true,
    TokenCount = 150,
    MetadataJson = "{\"language\": \"csharp\", \"complexity\": \"medium\"}"
};

await documentRepository.AddAsync(newDocument);
logger.LogInformation("✓ Added document: {Title} (ID: {DocumentId})", newDocument.Title, newDocument.Id);

// Example 2: Retrieve the document
logger.LogInformation("=== Example 2: Retrieving a document ===");
var retrieved = await documentRepository.GetByIdAsync(newDocument.Id);
if (retrieved != null)
{
    logger.LogInformation("✓ Retrieved document: {Title}", retrieved.Title);
    logger.LogInformation("  Repository: {Repository}", retrieved.RepoUrl);
    logger.LogInformation("  File: {FilePath}", retrieved.FilePath);
    logger.LogInformation("  Created: {CreatedAt:yyyy-MM-dd HH:mm:ss}", retrieved.CreatedAt);
}

// Example 3: Vector search
logger.LogInformation("=== Example 3: Vector similarity search ===");
var queryEmbedding = GenerateSampleEmbedding();
var similarDocuments = await vectorStore.QueryNearestAsync(queryEmbedding, k: 5);
logger.LogInformation("✓ Found {DocumentCount} documents similar to query", similarDocuments.Count);
foreach (var doc in similarDocuments)
{
    logger.LogInformation("  - {Title} ({FilePath})", doc.Title, doc.FilePath);
}

// Example 4: Get all documents from repository
logger.LogInformation("=== Example 4: Listing documents from repository ===");
var repoDocuments = await documentRepository.GetByRepoAsync(newDocument.RepoUrl);
logger.LogInformation("✓ Found {DocumentCount} documents in repository", repoDocuments.Count);
foreach (var doc in repoDocuments)
{
    logger.LogInformation("  - {Title}", doc.Title);
}

// Example 5: Count documents
logger.LogInformation("=== Example 5: Counting documents ===");
int totalCount = await vectorStore.CountAsync();
int repoCount = await vectorStore.CountAsync(newDocument.RepoUrl);
logger.LogInformation("✓ Total documents: {TotalCount}", totalCount);
logger.LogInformation("✓ Documents in this repo: {RepoCount}", repoCount);

// Example 6: Update a document
logger.LogInformation("=== Example 6: Updating a document ===");
retrieved!.Title = "Updated: Main Program File";
retrieved.Text = "Updated content with more information";
await documentRepository.UpdateAsync(retrieved);
logger.LogInformation("✓ Updated document: {Title}", retrieved.Title);

logger.LogInformation("=== Demonstration Complete ===");
logger.LogInformation("Configuration loaded from: User Secrets, Environment Variables, or appsettings.json");
logger.LogInformation("For more features, see the documentation in docs/");

// Example helper function to generate sample embeddings
static ReadOnlyMemory<float> GenerateSampleEmbedding()
{
    var embedding = new float[1536];
    var random = new Random();
    
    // Generate random vector (in real usage, this would come from an embedding API)
    for (int i = 0; i < embedding.Length; i++)
    {
        embedding[i] = (float)(random.NextDouble() - 0.5);
    }
    
    // Normalize to unit vector for cosine similarity
    var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
    if (norm > 0)
    {
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= norm;
        }
    }
    
    return new ReadOnlyMemory<float>(embedding);
}
