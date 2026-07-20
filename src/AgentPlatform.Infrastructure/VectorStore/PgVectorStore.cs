// ITextEmbeddingGenerationService is marked SKEXP0001 (experimental) in Semantic Kernel 1.x.
// This is the canonical embedding API; the experimental flag will be removed in a future SK release.
#pragma warning disable SKEXP0001

using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;
using Npgsql;
using Pgvector;

namespace AgentPlatform.Infrastructure.VectorStore;

/// <summary>
/// PostgreSQL pgvector-backed implementation of <see cref="IVectorStore"/> for document ingestion and similarity search.
/// Generates embeddings via Semantic Kernel's <see cref="ITextEmbeddingGenerationService"/> and performs
/// real cosine-distance similarity search against the <c>document_embeddings</c> table.
/// </summary>
internal sealed class PgVectorStore : IVectorStore, IDisposable
{
    private readonly ILogger<PgVectorStore> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _tableInitialized;

    /// <summary>
    /// Embedding vector dimension. Must match the model used by
    /// <c>ITextEmbeddingGenerationService</c> (currently text-embedding-3-small = 1536).
    /// If the embedding model is changed, this value MUST be updated to match
    /// the new model's output dimension, and the existing table must be recreated.
    /// </summary>
    private const int EmbeddingDimension = 1536;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgVectorStore"/> class.
    /// </summary>
    /// <param name="logger">The logger used to capture vector store operations telemetry.</param>
    /// <param name="configuration">The application configuration from which the PostgreSQL connection string is read.</param>
    /// <param name="embeddingService">The Semantic Kernel text-embedding service used to generate vector embeddings.</param>
    public PgVectorStore(
        ILogger<PgVectorStore> logger,
        IConfiguration configuration,
        ITextEmbeddingGenerationService embeddingService)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL is not configured. " +
                "Set it via dotnet user-secrets, environment variable, or appsettings.");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Generates an embedding for the given content and inserts it into the pgvector-backed
    /// <c>document_embeddings</c> table.
    /// </summary>
    public async Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Ingesting document {DocId} into collection {Collection}",
            documentId, collectionName);

        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            content, cancellationToken: ct);

        var metadataJson = metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(metadata)
            : "{}";

        await EnsureTableExistsAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO document_embeddings (id, collection_name, document_id, content, metadata, embedding, created_at)
            VALUES (@id, @collectionName, @documentId, @content, CAST(@metadata AS jsonb), @embedding, @createdAt)
            """, conn);

        cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@documentId", documentId);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@metadata", metadataJson);
        cmd.Parameters.AddWithValue("@embedding", new Vector(embedding.ToArray()));
        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug(
            "Successfully ingested document {DocId} into collection {Collection}",
            documentId, collectionName);
    }

    /// <summary>
    /// Generates an embedding for the query text and performs a real cosine-distance similarity search
    /// against the <c>document_embeddings</c> table, returning the topK most similar results.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, int topK = 5,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Searching collection {Collection} for: {Query} (topK={TopK})",
            collectionName, query, topK);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            query, cancellationToken: ct);

        await EnsureTableExistsAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT document_id, content, metadata, 1 - (embedding <=> @queryEmbedding) AS similarity
            FROM document_embeddings
            WHERE collection_name = @collectionName
            ORDER BY embedding <=> @queryEmbedding
            LIMIT @topK
            """, conn);

        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@queryEmbedding", new Vector(queryEmbedding.ToArray()));
        cmd.Parameters.AddWithValue("@topK", topK);

        var results = new List<VectorSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var docId = reader.GetString(0);
            var contentText = reader.GetString(1);
            var similarity = reader.GetDouble(3);

            Dictionary<string, string>? metaDict = null;
            if (!reader.IsDBNull(2))
            {
                var metaJson = reader.GetString(2);
                metaDict = JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
            }

            results.Add(new VectorSearchResult(docId, contentText, similarity, metaDict));
        }

        _logger.LogDebug(
            "Search in collection {Collection} returned {Count} result(s)",
            collectionName, results.Count);

        return results;
    }

    /// <summary>
    /// Deletes a document from the <c>document_embeddings</c> table by collection name and document id.
    /// </summary>
    public async Task DeleteDocumentAsync(string collectionName, string documentId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleting document {DocId} from collection {Collection}",
            documentId, collectionName);

        await EnsureTableExistsAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM document_embeddings
            WHERE collection_name = @collectionName AND document_id = @documentId
            """, conn);

        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@documentId", documentId);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);

        if (deleted > 0)
        {
            _logger.LogDebug(
                "Deleted document {DocId} from collection {Collection}",
                documentId, collectionName);
        }
        else
        {
            _logger.LogWarning(
                "Document {DocId} not found in collection {Collection} for deletion",
                documentId, collectionName);
        }
    }

    /// <summary>
    /// Creates the <c>document_embeddings</c> table and its indexes if they do not already exist.
    /// Called lazily on the first operation against a fresh database.
    /// </summary>
    private async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        if (_tableInitialized)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_tableInitialized)
                return;

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS document_embeddings (
                    id UUID PRIMARY KEY,
                    collection_name TEXT NOT NULL,
                    document_id TEXT NOT NULL,
                    content TEXT NOT NULL,
                    metadata JSONB DEFAULT '{}',
                    embedding vector(@dimension),
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS idx_doc_embeddings_collection
                    ON document_embeddings(collection_name);
                CREATE INDEX IF NOT EXISTS idx_doc_embeddings_document
                    ON document_embeddings(collection_name, document_id);
                """, conn);

            cmd.Parameters.AddWithValue("@dimension", EmbeddingDimension);
            await cmd.ExecuteNonQueryAsync(ct);

            _tableInitialized = true;
            _logger.LogInformation("Ensured document_embeddings table exists");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _dataSource.Dispose();
        _initLock.Dispose();
    }
}
