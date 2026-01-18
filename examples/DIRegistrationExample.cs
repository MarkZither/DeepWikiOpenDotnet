using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Console application demonstrating DeepWiki data access layer setup with dependency injection.
/// 
/// This example shows how to:
/// 1. Register SQL Server data layer services
/// 2. Add documents to the database
/// 3. Query documents by similarity
/// 4. Switch between SQL Server and PostgreSQL
/// </summary>
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Configure which database to use (SQL Server in this example)
        var databaseType = context.Configuration["DatabaseType"] ?? "SqlServer";
        var connectionString = context.Configuration["ConnectionStrings:DefaultConnection"]
            ?? "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword;Encrypt=false;";

        if (databaseType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // Register SQL Server data layer
            services.AddSqlServerDataLayer(connectionString);
            Console.WriteLine("Configured for SQL Server");
        }
        else if (databaseType.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Register PostgreSQL data layer
            // Uncomment when you have PostgreSQL available:
            // var postgresServices = new ServiceCollection();
            // postgresServices.AddPostgresDataLayer(connectionString);
            // services.Add(postgresServices);
            throw new NotImplementedException("PostgreSQL example not yet shown. See DI configuration docs.");
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

Console.WriteLine("DeepWiki Data Access Layer - DI Example\n");

// Example 1: Add a document
Console.WriteLine("=== Example 1: Adding a document ===");
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
Console.WriteLine($"✓ Added document: {newDocument.Title} (ID: {newDocument.Id})");

// Example 2: Retrieve the document
Console.WriteLine("\n=== Example 2: Retrieving a document ===");
var retrieved = await documentRepository.GetByIdAsync(newDocument.Id);
if (retrieved != null)
{
    Console.WriteLine($"✓ Retrieved document: {retrieved.Title}");
    Console.WriteLine($"  Repository: {retrieved.RepoUrl}");
    Console.WriteLine($"  File: {retrieved.FilePath}");
    Console.WriteLine($"  Created: {retrieved.CreatedAt:yyyy-MM-dd HH:mm:ss}");
}

// Example 3: Vector search
Console.WriteLine("\n=== Example 3: Vector similarity search ===");
var queryEmbedding = GenerateSampleEmbedding();
var similarDocuments = await vectorStore.QueryNearestAsync(queryEmbedding, k: 5);
Console.WriteLine($"✓ Found {similarDocuments.Count} documents similar to query");
foreach (var doc in similarDocuments)
{
    Console.WriteLine($"  - {doc.Title} ({doc.FilePath})");
}

// Example 4: Get all documents from repository
Console.WriteLine("\n=== Example 4: Listing documents from repository ===");
var repoDocuments = await documentRepository.GetByRepoAsync(newDocument.RepoUrl);
Console.WriteLine($"✓ Found {repoDocuments.Count} documents in repository");
foreach (var doc in repoDocuments)
{
    Console.WriteLine($"  - {doc.Title}");
}

// Example 5: Count documents
Console.WriteLine("\n=== Example 5: Counting documents ===");
int totalCount = await vectorStore.CountAsync();
int repoCount = await vectorStore.CountAsync(newDocument.RepoUrl);
Console.WriteLine($"✓ Total documents: {totalCount}");
Console.WriteLine($"✓ Documents in this repo: {repoCount}");

// Example 6: Update a document
Console.WriteLine("\n=== Example 6: Updating a document ===");
retrieved!.Title = "Updated: Main Program File";
retrieved.Text = "Updated content with more information";
await documentRepository.UpdateAsync(retrieved);
Console.WriteLine($"✓ Updated document: {retrieved.Title}");

Console.WriteLine("\n=== Demonstration Complete ===");
Console.WriteLine("This example shows basic CRUD operations and vector search.");
Console.WriteLine("For more features, see the documentation in docs/");

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
