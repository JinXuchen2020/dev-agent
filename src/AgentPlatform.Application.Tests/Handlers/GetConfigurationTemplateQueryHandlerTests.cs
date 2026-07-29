using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.AgentConfigurationManagement;
using AgentPlatform.Application.AgentConfigurationManagement.Queries.GetConfigurationTemplate;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class GetConfigurationTemplateQueryHandlerTests
{
    private readonly IAgentConfigurationRepository _repository = Substitute.For<IAgentConfigurationRepository>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();
    private readonly IYamlConfigurationParser _yamlParser = Substitute.For<IYamlConfigurationParser>();
    private readonly GetConfigurationTemplateQueryHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetConfigurationTemplateQueryHandlerTests()
    {
        _tenantProvider.GetTenantId().Returns(_tenantId);
        _handler = new GetConfigurationTemplateQueryHandler(_repository, _tenantProvider, _yamlParser);
    }

    private static AgentConfiguration BuildConfig(Guid tenantId, string yaml) =>
        new(Guid.NewGuid(), "My Template", yaml, tenantId,
            version: ConfigurationVersion.Initial,
            description: "A template description");

    [Fact]
    public async Task Handle_Should_Map_All_Fields_When_Yaml_Complete()
    {
        var config = BuildConfig(_tenantId, "role/model yaml");
        _repository.GetByIdAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        _yamlParser.Parse(Arg.Any<string>()).Returns(new Dictionary<string, object?>
        {
            ["agent_role"] = "developer",
            ["system_prompt"] = "You are helpful.",
            ["model"] = new Dictionary<string, object?>
            {
                ["provider"] = "openai",
                ["name"] = "gpt-4o",
                ["api_url"] = "https://api.openai.com/v1"
            }
        });

        var result = await _handler.Handle(new GetConfigurationTemplateQuery(config.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(config.Id, result!.ConfigurationId);
        Assert.Equal("My Template", result.Name);
        Assert.Equal("A template description", result.Description);
        Assert.Equal("developer", result.RoleCode);
        Assert.Equal("openai", result.ModelProvider);
        Assert.Equal("gpt-4o", result.ModelName);
        Assert.Equal("https://api.openai.com/v1", result.ModelApiUrl);
        Assert.Equal("You are helpful.", result.SystemPrompt);
        Assert.Equal("1.0.0", result.SourceVersion);
    }

    [Fact]
    public async Task Handle_Should_Leave_Model_Fields_Null_When_Model_Node_Missing()
    {
        var config = BuildConfig(_tenantId, "partial yaml");
        _repository.GetByIdAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        _yamlParser.Parse(Arg.Any<string>()).Returns(new Dictionary<string, object?>
        {
            ["agent_role"] = "architect",
            ["system_prompt"] = "Design systems."
        });

        var result = await _handler.Handle(new GetConfigurationTemplateQuery(config.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("architect", result!.RoleCode);
        Assert.Equal("Design systems.", result.SystemPrompt);
        Assert.Null(result.ModelProvider);
        Assert.Null(result.ModelName);
        Assert.Null(result.ModelApiUrl);
    }

    [Fact]
    public async Task Handle_Should_Return_Null_When_Config_NotFound()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((AgentConfiguration?)null);

        var result = await _handler.Handle(new GetConfigurationTemplateQuery(id), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_Should_Return_Null_On_Cross_Tenant_Id()
    {
        var config = BuildConfig(Guid.NewGuid(), "yaml"); // different tenant
        _repository.GetByIdAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _handler.Handle(new GetConfigurationTemplateQuery(config.Id), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_Should_Degrade_To_Metadata_Only_When_Yaml_Malformed()
    {
        var config = BuildConfig(_tenantId, "not valid");
        _repository.GetByIdAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        _yamlParser.Parse(Arg.Any<string>()).Returns(_ => throw new ArgumentException("bad yaml"));

        var result = await _handler.Handle(new GetConfigurationTemplateQuery(config.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("My Template", result!.Name);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Null(result.RoleCode);
        Assert.Null(result.SystemPrompt);
    }
}
