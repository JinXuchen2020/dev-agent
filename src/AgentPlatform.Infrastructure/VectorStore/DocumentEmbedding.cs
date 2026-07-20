namespace AgentPlatform.Infrastructure.VectorStore;

/// <summary>
/// Represents a document embedding row in the pgvector-backed document_embeddings table.
/// </summary>
public sealed class DocumentEmbedding
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; init; }

    /// <summary>The logical collection or namespace this document belongs to.</summary>
    public string CollectionName { get; init; } = string.Empty;

    /// <summary>The application-level identifier of the document.</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The raw text content of the document.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Optional metadata serialized as JSON.</summary>
    public string Metadata { get; init; } = "{}";

    /// <summary>The embedding vector generated from <see cref="Content"/>.</summary>
    public float[] Embedding { get; init; } = [];

    /// <summary>The timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; init; }
}
