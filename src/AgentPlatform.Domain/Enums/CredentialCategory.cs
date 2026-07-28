namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Category of a tenant-provided external API credential.
/// Both categories reuse the same encryption, tenant isolation, RBAC, and masking machinery.
/// </summary>
public enum CredentialCategory
{
    /// <summary>LLM model provider credential (OpenAI-compatible: OpenAI / DeepSeek / VLLM / Custom).</summary>
    Model = 0,

    /// <summary>Web search provider credential (v1: SerpApi only).</summary>
    Search = 1
}
