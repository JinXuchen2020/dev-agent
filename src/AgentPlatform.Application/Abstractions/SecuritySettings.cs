namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Security configuration settings for authentication, authorization, and encryption.
/// </summary>
public sealed class SecuritySettings
{
    /// <summary>
    /// Gets or sets the JWT issuer that must match the token's "iss" claim.
    /// </summary>
    public string JwtIssuer { get; set; } = "agent-platform";

    /// <summary>
    /// Gets or sets the JWT audience that must match the token's "aud" claim.
    /// </summary>
    public string JwtAudience { get; set; } = "agent-platform-api";

    /// <summary>
    /// Gets or sets the symmetric key used to sign and validate JWT tokens.
    /// Must be at least 32 bytes (256 bits) for HS256.
    /// </summary>
    public string JwtSecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the header name used for API-Key authentication.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Gets or sets the AES-256-GCM encryption key (hex-encoded, 64 hex chars = 32 bytes).
    /// </summary>
    public string AesEncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether authentication is enforced. When false (e.g., dev/quickstart),
    /// all requests are allowed without credentials.
    /// </summary>
    public bool EnforceAuthentication { get; set; } = true;

    /// <summary>
    /// Gets or sets the default rate limit per minute for authenticated requests.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 100;
}

/// <summary>
/// Role names for RBAC.
/// </summary>
public static class RoleNames
{
    /// <summary>Full administrative access to all platform features.</summary>
    public const string Admin = "Admin";

    /// <summary>Operational access for day-to-day workflow management.</summary>
    public const string Operator = "Operator";

    /// <summary>Read-only access to view resources without modification rights.</summary>
    public const string Viewer = "Viewer";
}
