using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.ValueObjects;
using Xunit;

namespace AgentPlatform.ArchitectureTests;

/// <summary>
/// Enforces the F19 contract: the hard-coded <see cref="AgentType"/> value-object mirror and the
/// database-authoritative built-in role catalog (<see cref="BuiltInRoleCatalog"/>) must never drift
/// apart. Changing one side without the other fails this test.
/// </summary>
public sealed class AgentRoleCatalogParityTests
{
    [Fact]
    public void AgentType_PredefinedCodes_ShouldMatchBuiltInRoleCatalog()
    {
        var valueObjectCodes = AgentType.Predefined
            .Select(x => x.RoleCode)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToHashSet();

        var catalogCodes = BuiltInRoleCatalog.Codes
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToHashSet();

        Assert.Equal(catalogCodes, valueObjectCodes);
    }

    [Fact]
    public void BuiltInRoleCatalog_ShouldContainExactlySevenRoles()
    {
        Assert.Equal(7, BuiltInRoleCatalog.Codes.Count);
    }

    [Fact]
    public void AgentType_FromCode_ShouldResolveEveryBuiltInCode()
    {
        foreach (var code in BuiltInRoleCatalog.Codes)
        {
            var resolved = AgentType.FromCode(code);
            Assert.NotNull(resolved);
            Assert.Equal(code, resolved!.RoleCode);
        }
    }
}
