using System;

namespace DeepWiki.Data.Abstractions.Models
{
    public class DocumentDto
    {
        public Guid Id { get; set; }
        public string RepoUrl { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public string MetadataJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int TokenCount { get; set; }
        public string FileType { get; set; } = string.Empty;
        public bool IsCode { get; set; }
        public bool IsImplementation { get; set; }

        /// <summary>
        /// Zero-based index of this chunk within the parent document (default 0).
        /// </summary>
        public int ChunkIndex { get; set; } = 0;

        /// <summary>
        /// Total number of chunks the source document was split into (default 1).
        /// </summary>
        public int TotalChunks { get; set; } = 1;
    }
}