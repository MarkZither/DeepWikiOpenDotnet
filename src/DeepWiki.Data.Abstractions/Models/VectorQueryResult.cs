using System;

namespace DeepWiki.Data.Abstractions.Models
{
    public class VectorQueryResult
    {
        public DocumentDto Document { get; set; } = new DocumentDto();
        public float SimilarityScore { get; set; }
    }
}