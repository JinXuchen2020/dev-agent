namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for ingesting, searching, and deleting documents in a vector store.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Ingests a document into the specified vector collection.
    /// </summary>
    /// <param name="collectionName">The name of the vector collection to ingest into.</param>
    /// <param name="documentId">The unique identifier of the document.</param>
    /// <param name="content">The text content to embed and store.</param>
    /// <param name="metadata">Optional metadata key-value pairs associated with the document.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous ingest operation.</returns>
    Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Performs a semantic similarity search against the specified vector collection.
    /// </summary>
    /// <param name="collectionName">The name of the vector collection to search.</param>
    /// <param name="query">The natural-language query to search for.</param>
    /// <param name="topK">The maximum number of results to return. Defaults to 5.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A read-only list of search results ranked by relevance.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, int topK = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a document from the specified vector collection.
    /// </summary>
    /// <param name="collectionName">The name of the vector collection containing the document.</param>
    /// <param name="documentId">The unique identifier of the document to delete.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous delete operation.</returns>
    Task DeleteDocumentAsync(string collectionName, string documentId,
        CancellationToken ct = default);
}

/// <summary>
/// Represents a single result returned from a vector similarity search.
/// </summary>
/// <param name="DocumentId">The unique identifier of the matched document.</param>
/// <param name="Content">The text content of the matched document.</param>
/// <param name="Score">The relevance score of the match, where higher values indicate greater similarity.</param>
/// <param name="Metadata">Optional metadata key-value pairs associated with the matched document.</param>
public record VectorSearchResult(
    string DocumentId,
    string Content,
    double Score,
    Dictionary<string, string>? Metadata = null);
