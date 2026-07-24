using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgentPlatform.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AgentPlatform.Api.Security;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs. Key/issuer/audience are read from the
/// <c>Security</c> configuration section (mirrors the previous dev-login logic).
/// Implemented in the Api layer because it depends on the JWT signing package.
/// </summary>
internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(IEnumerable<Claim> claims, TimeSpan? lifetime = null)
    {
        var securitySection = _configuration.GetSection("Security");
        var jwtKey = securitySection["JwtSecretKey"] ?? "dev-secret-key-min-32-chars-long!!";
        var issuer = securitySection["JwtIssuer"] ?? "agent-platform";
        var audience = securitySection["JwtAudience"] ?? "agent-platform-api";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
