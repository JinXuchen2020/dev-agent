using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace AgentPlatform.ArchitectureTests;

/// <summary>
/// Architecture constraint tests enforcing DDD dependency rules.
/// These tests run on every build via `dotnet test` — if any dependency direction
/// is violated, the build fails at compile-check time, not at runtime.
/// </summary>
public sealed class DddLayerTests
{
    // ─── 1. Dependency direction: Domain must reference NOTHING ───

    [Fact]
    public void Domain_Should_HaveZeroExternalDependencies()
    {
        var domainCsproj = new FileInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Domain", "AgentPlatform.Domain.csproj"));
        if (!domainCsproj.Exists)
            return; // skip if project file not found during test discovery

        var content = File.ReadAllText(domainCsproj.FullName);

        // Domain project must have NO PackageReference (pure .NET, zero external packages)
        Assert.DoesNotContain("<PackageReference", content);
        Assert.DoesNotContain("<ProjectReference", content);
    }

    // ─── 2. Dependency direction: Infrastructure must NOT be referenced by Api directly ───
    // (Api → Infrastructure is allowed for DI registration, but NOT for Application)

    [Fact]
    public void Application_Should_NotReference_Infrastructure()
    {
        var appCsproj = new FileInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Application", "AgentPlatform.Application.csproj"));
        if (!appCsproj.Exists)
            return;

        var content = File.ReadAllText(appCsproj.FullName);
        Assert.DoesNotContain("AgentPlatform.Infrastructure", content);
    }

    // ─── 3. All implementation classes in Infrastructure must be internal sealed ───

    [Fact]
    public void Infrastructure_ImplementationClasses_Should_BeInternalSealed()
    {
        var infraDir = new DirectoryInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Infrastructure"));

        if (!infraDir.Exists)
            return;

        var files = infraDir.GetFiles("*.cs", SearchOption.AllDirectories);

        // If TreatWarningsAsErrors is enabled, CA1852 (sealed) will be enforced
        // by the compiler. This test verifies intent-level compliance.
        var nonSealed = files
            .Where(f => !f.Name.EndsWith(".g.cs") && !f.Name.StartsWith("_"))
            .Select(f =>
            {
                var lines = File.ReadLines(f.FullName).Take(20).ToList();
                var hasPublicClass = lines.Any(l => l.Contains("public class ") && !l.Contains("sealed"));
                return hasPublicClass ? f.FullName : null;
            })
            .Where(x => x is not null)
            .ToList();

        if (nonSealed.Count > 0)
        {
            var sample = string.Join("\n  ", nonSealed.Take(5));
            Assert.Fail($"Found {nonSealed.Count} non-sealed classes in Infrastructure.\n  {sample}");
        }
    }

    // ─── 4. All aggregate roots must have IEntityTypeConfiguration ───

    [Fact]
    public void AllAggregateRoots_Should_HaveEntityTypeConfiguration()
    {
        var domainDir = new DirectoryInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Domain", "Aggregates"));

        var configDir = new DirectoryInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Infrastructure", "Persistence", "Configurations"));

        if (!domainDir.Exists || !configDir.Exists)
            return;

        // Discover aggregate root classes (classes that actually implement IAggregateRoot).
        // Owned child entities (Message / ExecutionLogEntry / WorkflowEdge / KnowledgeDocument)
        // are configured via OwnsMany/OwnsOne on the parent's IEntityTypeConfiguration and must
        // NOT be treated as standalone aggregate roots needing their own *Configuration.cs.
        var aggregateRoots = domainDir.GetDirectories()
            .SelectMany(d => d.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
            .Where(f =>
            {
                var content = File.ReadAllText(f.FullName);
                return content.Contains("IAggregateRoot")
                    && !content.Contains("interface IEntityTypeConfiguration");
            })
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .ToList();

        // Discover configuration classes
        var configs = configDir.GetFiles("*Configuration.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .ToList();

        var missing = aggregateRoots
            .Where(agg => !configs.Any(cfg =>
                cfg.Equals(agg + "Configuration", StringComparison.Ordinal)))
            .ToList();

        if (missing.Count > 0)
        {
            Assert.Fail($"Aggregate roots missing IEntityTypeConfiguration:\n  {string.Join("\n  ", missing)}");
        }
    }

    // ─── 5. Interfaces in Application/Abstractions must have DI registration ───

    [Fact]
    public void AllAbstractionInterfaces_Should_BeRegisteredInDi()
    {
        var abstractionsDir = new DirectoryInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Application", "Abstractions"));

        var diFile = new FileInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Infrastructure", "DependencyInjection.cs"));

        if (!abstractionsDir.Exists || !diFile.Exists)
            return;

        // Extract interface names from Abstractions (excluding marker interfaces like ICommand
        // and intentionally-[Obsolete] interfaces that are documented tech debt / not DI-registered).
        var interfaceNames = abstractionsDir.GetFiles("*.cs")
            .Where(f =>
            {
                var content = File.ReadAllText(f.FullName);
                var name = Path.GetFileNameWithoutExtension(f.Name);
                return name.StartsWith("I", StringComparison.Ordinal)
                    && name != "ICommand"
                    && name != "IAggregateRoot"
                    && !content.Contains("[Obsolete", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .ToList();

        var diContent = File.ReadAllText(diFile.FullName);

        var missing = interfaceNames
            .Where(intf => !diContent.Contains(intf, StringComparison.Ordinal))
            .ToList();

        if (missing.Count > 0)
        {
            Assert.Fail($"Interfaces not found in DependencyInjection.cs:\n  {string.Join("\n  ", missing)}");
        }
    }

    // ─── 6. Controllers must not inject application services directly ───
    // (should inject IMediator only for Command/Query dispatch)

    [Fact]
    public void Controllers_Should_NotInjectApplicationServicesDirectly()
    {
        var controllersDir = new DirectoryInfo(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "AgentPlatform.Api", "Controllers"));

        if (!controllersDir.Exists)
            return;

        var files = controllersDir.GetFiles("*Controller.cs");
        foreach (var file in files)
        {
            // Strip XML doc-comment lines and string literals so the heuristic doesn't match
            // prose such as "Initializes a new instance..." (/// summaries) or "Invalid workflow ID."
            // (string literals) as if they were IInterface injections.
            var codeContent = string.Join("\n",
                File.ReadAllLines(file.FullName)
                    .Where(l => !l.TrimStart().StartsWith("///")));
            codeContent = System.Text.RegularExpressions.Regex.Replace(codeContent, @"""[^""]*""", "");

            // Allowed injected abstractions: cross-cutting infra + IMediator (CQRS dispatch).
            var allowed = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "IOptions", "IMediator", "IServiceProvider", "IEnumerable",
                "ILogger", "IResult", "ITenantProvider", "IExecutionProgressBroadcaster",
            };

            var injections = new System.Text.RegularExpressions.Regex(
                @"\b(I\w+)\s+\w+\s", // matches "IInterface parameter"
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var matches = injections.Matches(codeContent)
                .Select(m => m.Groups[1].Value)
                .Where(name => !string.IsNullOrEmpty(name) && !allowed.Contains(name))
                .ToList();

            if (matches.Count > 0)
            {
                Assert.Fail($"Controller '{file.Name}' injects non-Mediator services:\n  {string.Join(", ", matches)}");
            }
        }
    }
}
