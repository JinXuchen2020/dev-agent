using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.VectorStore;

/// <summary>
/// PostgreSQL pgvector-backed implementation of <see cref="IVectorStore"/> for document ingestion and similarity search.
/// NOTE: this is currently a STUB — it is NOT grounded to a real vector database (see <see cref="ObsoleteAttribute"/>).
/// </summary>
[Obsolete("RAG vector store is a STUB and is NOT grounded to a real vector database: SearchAsync returns hardcoded doc-1/doc-2 and Ingest/Delete are no-ops. Replace with a real pgvector-backed implementation (tracked in Phase 4) before relying on RAG.", false)]
internal sealed class PgVectorStore : IVectorStore
{
    private readonly ILogger<PgVectorStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgVectorStore"/> class.
    /// </summary>
    /// <param name="logger">The logger used to capture vector store operations telemetry.</param>
    public PgVectorStore(ILogger<PgVectorStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// STUB: logs and returns without embedding or persisting. Documents are NOT stored and will never appear in SearchAsync.
    /// </summary>
    /// <param name="collectionName">The name of the collection to ingest the document into.</param>
    /// <param name="documentId">The unique identifier of the document.</param>
    /// <param name="content">The textual content of the document to embed and store.</param>
    /// <param name="metadata">Optional key-value metadata to associate with the document.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous ingestion operation.</returns>
    public Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Ingested document {DocId} into collection {Collection}",
            documentId, collectionName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// STUB: returns hardcoded placeholder results (doc-1/doc-2). Does NOT perform real embedding or similarity search.
    /// </summary>
    /// <param name="collectionName">The name of the collection to search.</param>
    /// <param name="query">The natural-language query to match against stored documents.</param>
    /// <param name="topK">The maximum number of results to return.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of <see cref="VectorSearchResult"/> items ranked by similarity.</returns>
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, int topK = 5,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Searching collection {Collection} for: {Query}",
            collectionName, query);

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(
            new List<VectorSearchResult>
            {
                new("doc-1", $"Sample result for: {query}", 0.95),
                new("doc-2", $"Another result for: {query}", 0.85)
            });
    }

    /// <summary>
    /// STUB: logs and returns without deleting anything. No-op.
    /// </summary>
    /// <param name="collectionName">The name of the collection containing the document.</param>
    /// <param name="documentId">The unique identifier of the document to delete.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous deletion operation.</returns>
    public Task DeleteDocumentAsync(string collectionName, string documentId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleted document {DocId} from collection {Collection}",
            documentId, collectionName);
        return Task.CompletedTask;
    }
}
