using AgentPlatform.Application.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentPlatform.Infrastructure.Configuration;

/// <summary>
/// YamlDotNet-based implementation of <see cref="IYamlConfigurationParser"/> for parsing, serializing,
/// and validating YAML agent configuration content.
/// </summary>
internal sealed class YamlConfigurationParserService : IYamlConfigurationParser
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public YamlConfigurationParserService()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Parse(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        try
        {
            var result = _deserializer.Deserialize<Dictionary<string, object?>>(yamlContent);
            return result ?? new Dictionary<string, object?>();
        }
        catch (YamlException ex)
        {
            throw new ArgumentException($"Failed to parse YAML content: {ex.Message}", nameof(yamlContent), ex);
        }
    }

    /// <inheritdoc />
    public string Serialize(IReadOnlyDictionary<string, object?> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            return _serializer.Serialize(data);
        }
        catch (YamlException ex)
        {
            throw new ArgumentException($"Failed to serialize data to YAML: {ex.Message}", nameof(data), ex);
        }
    }

    /// <inheritdoc />
    public bool Validate(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            return false;

        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(yamlContent);
            yamlStream.Load(reader);
            return true;
        }
        catch (YamlException)
        {
            return false;
        }
    }
}
