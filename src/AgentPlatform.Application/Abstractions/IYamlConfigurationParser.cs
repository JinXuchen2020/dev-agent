namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides YAML parsing and serialization services for agent configuration content.
/// </summary>
public interface IYamlConfigurationParser
{
    /// <summary>
    /// Parses the specified YAML string into a strongly-typed dictionary representation.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A dictionary representing the parsed YAML structure.</returns>
    /// <exception cref="ArgumentException">Thrown when the YAML content is null, empty, or malformed.</exception>
    IReadOnlyDictionary<string, object?> Parse(string yamlContent);

    /// <summary>
    /// Serializes a dictionary representation back into a YAML string.
    /// </summary>
    /// <param name="data">The data to serialize.</param>
    /// <returns>A YAML-formatted string.</returns>
    string Serialize(IReadOnlyDictionary<string, object?> data);

    /// <summary>
    /// Validates whether the specified YAML content is syntactically valid.
    /// </summary>
    /// <param name="yamlContent">The YAML content to validate.</param>
    /// <returns><c>true</c> if the YAML content is valid; otherwise <c>false</c>.</returns>
    bool Validate(string yamlContent);
}
