using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AgentPlatform.Infrastructure.Credentials;

/// <summary>
/// Exposes the platform model catalog to all tenants. The catalog is DB-backed
/// (<c>PlatformModels</c> table, non-tenant-scoped) so operators can manage default models
/// at runtime via the admin surface. When the table is empty (e.g. fresh dev / QuickStart
/// before any seed), it falls back to a single candidate derived from the <c>OpenAI:*</c>
/// configuration so the platform still routes out-of-the-box — preserving the legacy
/// <c>RouterSettings.Candidates</c> behavior without keeping that config key.
/// </summary>
internal sealed class PlatformModelsProvider : IPlatformModelProvider
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public PlatformModelsProvider(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public IReadOnlyList<ModelCandidate> GetCandidates()
    {
        var models = _db.PlatformModels
            .IgnoreQueryFilters()
            .Where(m => m.IsEnabled)
            .OrderByDescending(m => m.Priority)
            .ToList();

        if (models.Count == 0)
        {
            // DB-empty fallback (legacy RouterSettings.Candidates behavior): derive a single
            // candidate from the OpenAI:* configuration so routing works before any seed.
            var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
            return new List<ModelCandidate> { new(model, "openai", 100) };
        }

        return models
            .Select(m => new ModelCandidate(m.ModelName, m.Provider, m.Priority))
            .ToList();
    }
}
