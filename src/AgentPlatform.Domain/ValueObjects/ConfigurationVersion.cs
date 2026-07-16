namespace AgentPlatform.Domain.ValueObjects;

/// <summary>
/// Represents a semantic version identifier for an agent configuration,
/// including optional change log notes.
/// </summary>
public sealed record ConfigurationVersion
{
    /// <summary>
    /// Gets the major version number. Incremented for breaking changes.
    /// </summary>
    public int Major { get; private init; }

    /// <summary>
    /// Gets the minor version number. Incremented for backward-compatible feature additions.
    /// </summary>
    public int Minor { get; private init; }

    /// <summary>
    /// Gets the patch version number. Incremented for backward-compatible bug fixes.
    /// </summary>
    public int Patch { get; private init; }

    /// <summary>
    /// Gets an optional description of changes in this version.
    /// </summary>
    public string ChangeLog { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationVersion"/> record.
    /// </summary>
    /// <param name="major">The major version number.</param>
    /// <param name="minor">The minor version number.</param>
    /// <param name="patch">The patch version number.</param>
    /// <param name="changeLog">An optional description of changes in this version.</param>
    public ConfigurationVersion(int major, int minor, int patch, string? changeLog = null)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major), "Major version cannot be negative.");
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor), "Minor version cannot be negative.");
        if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch), "Patch version cannot be negative.");

        Major = major;
        Minor = minor;
        Patch = patch;
        ChangeLog = changeLog ?? string.Empty;
    }

    /// <summary>
    /// Gets the version 1.0.0, representing the initial version.
    /// </summary>
    public static ConfigurationVersion Initial => new(1, 0, 0, "Initial version");

    /// <summary>
    /// Returns the semantic version string in the format "major.minor.patch".
    /// </summary>
    /// <returns>A string representation of the version.</returns>
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
