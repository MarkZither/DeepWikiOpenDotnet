using DeepWiki.Data.Entities;
using Microsoft.Data.SqlTypes;

namespace DeepWiki.Data.SqlServer.Seeds;

/// <summary>
/// Initial seed data for testing and development.
/// Contains sample documents with realistic embeddings for vector search testing.
/// </summary>
public static class InitialSeedData
{
    /// <summary>
    /// Generate seed documents for testing.
    /// Each document has a realistic 1536-dimensional embedding (typical for modern embedding models).
    /// </summary>
    public static IEnumerable<DocumentEntity> GetSeedDocuments()
    {
        // Sample embedding 1: "database design patterns"
        var embedding1 = CreateSampleEmbedding("database design patterns");
        
        // Sample embedding 2: "query optimization techniques"
        var embedding2 = CreateSampleEmbedding("query optimization techniques");
        
        // Sample embedding 3: "distributed systems architecture"
        var embedding3 = CreateSampleEmbedding("distributed systems architecture");
        
        return new[]
        {
            new DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/sample/database-guide",
                FilePath = "docs/design-patterns.md",
                Title = "Database Design Patterns",
                Text = "This document covers fundamental database design patterns including normalization, denormalization, and when to use each approach in production systems.",
                Embedding = embedding1,
                FileType = "md",
                IsCode = false,
                IsImplementation = false,
                TokenCount = 250,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                MetadataJson = "{\"tags\":[\"database\",\"design\",\"patterns\"],\"difficulty\":\"intermediate\"}"
            },
            new DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/sample/database-guide",
                FilePath = "src/QueryOptimizer.cs",
                Title = "Query Optimization Implementation",
                Text = "C# implementation of query optimization techniques including index strategies, execution plan analysis, and query rewriting patterns used in high-performance database applications.",
                Embedding = embedding2,
                FileType = "cs",
                IsCode = true,
                IsImplementation = true,
                TokenCount = 400,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                MetadataJson = "{\"tags\":[\"performance\",\"optimization\",\"sql\"],\"difficulty\":\"advanced\"}"
            },
            new DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/sample/distributed-systems",
                FilePath = "architecture/README.md",
                Title = "Distributed Systems Architecture Guide",
                Text = "Comprehensive guide to building scalable distributed systems including consensus algorithms, replication strategies, failure detection, and consistency models for modern cloud applications.",
                Embedding = embedding3,
                FileType = "md",
                IsCode = false,
                IsImplementation = false,
                TokenCount = 500,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                MetadataJson = "{\"tags\":[\"distributed\",\"scalability\",\"architecture\"],\"difficulty\":\"advanced\"}"
            }
        };
    }

    /// <summary>
    /// Create a sample 1536-dimensional embedding based on text.
    /// Uses a simple hash-based deterministic generation for testing.
    /// In production, use a real embedding model (OpenAI, Cohere, etc).
    /// </summary>
    private static ReadOnlyMemory<float> CreateSampleEmbedding(string text)
    {
        var embedding = new float[1536];
        var hash = text.GetHashCode();
        var random = new Random(hash);

        for (int i = 0; i < embedding.Length; i++)
        {
            // Generate normalized values in [-1, 1]
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize to unit vector (cosine similarity)
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
}
