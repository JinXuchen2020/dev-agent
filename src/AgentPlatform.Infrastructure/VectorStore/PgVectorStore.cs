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
/// 基于 PostgreSQL pgvector 的 <see cref="IVectorStore"/> 实现，用于文档入库与相似度检索。
/// 通过 Semantic Kernel 的 <see cref="ITextEmbeddingGenerationService"/> 生成向量，
/// 并针对 <c>document_embeddings</c> 表执行真实的余弦距离相似度检索。
/// 检索与入库均按租户隔离（tenant_id），并支持相关性阈值过滤。
/// </summary>
internal sealed class PgVectorStore : IVectorStore, IDisposable
{
    private readonly ILogger<PgVectorStore> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _tableInitialized;

    /// <summary>
    /// 向量维度，必须与 <c>ITextEmbeddingGenerationService</c> 使用的模型一致
    /// （当前为 text-embedding-3-small = 1536）。
    /// 若更换 embedding 模型，必须同步修改此值并重建表。
    /// </summary>
    private const int EmbeddingDimension = 1536;

    /// <summary>
    /// 初始化 <see cref="PgVectorStore"/> 的新实例。
    /// </summary>
    /// <param name="logger">用于记录向量存储操作的日志器。</param>
    /// <param name="configuration">用于读取 PostgreSQL 连接字符串的应用配置。</param>
    /// <param name="embeddingService">用于生成向量 embedding 的 Semantic Kernel 文本 embedding 服务。</param>
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
    /// 为给定内容生成 embedding 并插入 pgvector 支持的 <c>document_embeddings</c> 表（按租户隔离）。
    /// </summary>
    public async Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Guid tenantId,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Ingesting document {DocId} into collection {Collection} for tenant {Tenant}",
            documentId, collectionName, tenantId);

        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            content, cancellationToken: ct);

        var metadataJson = metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(metadata)
            : "{}";

        await EnsureTableExistsAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO document_embeddings (id, tenant_id, collection_name, document_id, content, metadata, embedding, created_at)
            VALUES (@id, @tenantId, @collectionName, @documentId, @content, CAST(@metadata AS jsonb), @embedding, @createdAt)
            """, conn);

        cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@documentId", documentId);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@metadata", metadataJson);
        cmd.Parameters.AddWithValue("@embedding", new Vector(embedding.ToArray()));
        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug(
            "Successfully ingested document {DocId} into collection {Collection} for tenant {Tenant}",
            documentId, collectionName, tenantId);
    }

    /// <summary>
    /// 为查询文本生成 embedding，并针对 <c>document_embeddings</c> 表执行真实余弦距离相似度检索，
    /// 按租户过滤并可在低于相关性阈值时剔除低分结果。
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, Guid tenantId,
        int topK = 5, double? minScore = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Searching collection {Collection} for tenant {Tenant}: {Query} (topK={TopK}, minScore={MinScore})",
            collectionName, tenantId, query, topK, minScore);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            query, cancellationToken: ct);

        await EnsureTableExistsAsync(ct);

        var whereClause = "WHERE collection_name = @collectionName AND tenant_id = @tenantId";
        if (minScore.HasValue)
            whereClause += " AND (1 - (embedding <=> @queryEmbedding)) >= @minScore";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT document_id, content, metadata, 1 - (embedding <=> @queryEmbedding) AS similarity
            FROM document_embeddings
            {whereClause}
            ORDER BY embedding <=> @queryEmbedding
            LIMIT @topK
            """, conn);

        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@queryEmbedding", new Vector(queryEmbedding.ToArray()));
        cmd.Parameters.AddWithValue("@topK", topK);
        if (minScore.HasValue)
            cmd.Parameters.AddWithValue("@minScore", minScore.Value);

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
            "Search in collection {Collection} for tenant {Tenant} returned {Count} result(s)",
            collectionName, tenantId, results.Count);

        return results;
    }

    /// <summary>
    /// 按集合名、文档标识与租户从 <c>document_embeddings</c> 表删除文档。
    /// </summary>
    public async Task DeleteDocumentAsync(string collectionName, string documentId,
        Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleting document {DocId} from collection {Collection} for tenant {Tenant}",
            documentId, collectionName, tenantId);

        await EnsureTableExistsAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM document_embeddings
            WHERE collection_name = @collectionName AND document_id = @documentId AND tenant_id = @tenantId
            """, conn);

        cmd.Parameters.AddWithValue("@collectionName", collectionName);
        cmd.Parameters.AddWithValue("@documentId", documentId);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);

        if (deleted > 0)
        {
            _logger.LogDebug(
                "Deleted document {DocId} from collection {Collection} for tenant {Tenant}",
                documentId, collectionName, tenantId);
        }
        else
        {
            _logger.LogWarning(
                "Document {DocId} not found in collection {Collection} for tenant {Tenant} for deletion",
                documentId, collectionName, tenantId);
        }
    }

    /// <summary>
    /// 在首次操作时惰性创建 <c>document_embeddings</c> 表及其索引（含 tenant_id 列），
    /// 并对已存在的旧表补加 tenant_id 列（向后兼容，避免迁移失败）。
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

            // 兼容已存在的旧表：先补列（IF NOT EXISTS 幂等），再建表（IF NOT EXISTS 幂等）。
            await using var alterCmd = new NpgsqlCommand(
                """
                ALTER TABLE document_embeddings
                ADD COLUMN IF NOT EXISTS tenant_id UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
                """, conn);
            await alterCmd.ExecuteNonQueryAsync(ct);

            await using var cmd = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS document_embeddings (
                    id UUID PRIMARY KEY,
                    tenant_id UUID NOT NULL,
                    collection_name TEXT NOT NULL,
                    document_id TEXT NOT NULL,
                    content TEXT NOT NULL,
                    metadata JSONB DEFAULT '{}',
                    embedding vector(@dimension),
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS idx_doc_embeddings_tenant
                    ON document_embeddings(tenant_id);
                CREATE INDEX IF NOT EXISTS idx_doc_embeddings_collection
                    ON document_embeddings(tenant_id, collection_name);
                CREATE INDEX IF NOT EXISTS idx_doc_embeddings_document
                    ON document_embeddings(tenant_id, collection_name, document_id);
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
