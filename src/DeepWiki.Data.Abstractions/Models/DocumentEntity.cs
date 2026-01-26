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
    }
}