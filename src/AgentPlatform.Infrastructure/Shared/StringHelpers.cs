namespace AgentPlatform.Infrastructure.Shared;

/// <summary>
/// Shared string utility methods used across the infrastructure layer.
/// </summary>
internal static class StringHelpers
{
    /// <summary>
    /// Truncates the specified string to the given maximum length.
    /// Returns the original string if its length does not exceed <paramref name="maxLength"/>.
    /// </summary>
    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
