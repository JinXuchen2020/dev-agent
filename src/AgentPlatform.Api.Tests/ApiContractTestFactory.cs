using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using AgentPlatform.Infrastructure.Persistence;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> for API contract tests.
///
/// Overrides configuration to use an in-memory SQLite database, configures
/// a known JWT secret key for test token generation, and uses stub model
/// clients so the full ASP.NET Core pipeline runs without external dependencies.
/// </summary>
public sealed class ApiContractTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// JWT secret key used for signing test tokens. Must be at least 32 characters
    /// and differ from the dev default to satisfy the startup guard in Program.cs.
    /// </summary>
    private const string TestJwtSecretKey = "test-only-secret-key-at-least-32-chars!!";

    private readonly SqliteConnection _sqliteConnection;

    /// <summary>
    /// Initializes a new factory instance and opens a shared in-memory
    /// SQLite connection that lives for the lifetime of the factory.
    /// </summary>
    public ApiContractTestFactory()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    /// <summary>
    /// Configures the web host to use the Test environment, overrides settings,
    /// and replaces the EF Core database provider with the in-memory SQLite
    /// connection shared across all requests.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use in-memory SQLite for the database
                ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                ["Database:Type"] = "sqlite",

                // Pin the default tenant so the seeded admin user, the tenant
                // resolved by TenantProvider at login, and the test Bearer token
                // (which carries tenant_id 00000000-0000-0000-0000-000000000001)
                // all agree. Without this, the login endpoint could resolve a
                // different default tenant than the one the seed wrote the user to.
                ["Tenant:DefaultTenantId"] = "00000000-0000-0000-0000-000000000001",

                // Valid JWT secret key (must differ from dev default)
                ["Security:JwtSecretKey"] = TestJwtSecretKey,
                ["Security:DevLoginEnabled"] = "false",

                // Keep authentication enforced so the real auth pipeline runs
                ["Security:EnforceAuthentication"] = "true",

                // Use in-memory cache to avoid Redis dependency
                ["Cache:Provider"] = "Memory",

                // Use stub model client to avoid real LLM API calls
                ["ModelClient:Provider"] = "Stub",
                ["ModelClient:StubResponse"] = "Contract test response.",

                // Provide a non-empty Key so the embedding service registration does not throw
                ["OpenAI:Key"] = "test-openai-key-not-empty",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the default DbContext options registration so we can
            // inject the shared in-memory SQLite connection.
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_sqliteConnection));

            // Create the database schema using a temporary scope so the
            // schema is ready before the first test request.
            // The same _sqliteConnection is used by the host's DI container,
            // so EnsureCreated runs against the same in-memory database.
            using var tempSp = services.BuildServiceProvider();
            using var tempScope = tempSp.CreateScope();
            var db = tempScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Factory initialization — ensures the server is started and database schema ready.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Ensure the database schema exists by creating a scope from
        // the host's DI container (which uses the shared _sqliteConnection).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await _sqliteConnection.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sqliteConnection?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> pre-configured with a valid
    /// Bearer JWT token in the default request headers.
    /// </summary>
    /// <param name="role">The role claim to include (default: "Admin").</param>
    /// <returns>An HttpClient that sends authenticated requests.</returns>
    public HttpClient CreateAuthenticatedClient(string role = "Admin")
    {
        var client = CreateClient();
        var token = GenerateTestToken(role);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Generates a JWT bearer token signed with the test secret key.
    /// Includes role, name identifier, and tenant claims.
    /// </summary>
    private static string GenerateTestToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", "00000000-0000-0000-0000-000000000001"),
        };

        var token = new JwtSecurityToken(
            issuer: "agent-platform",
            audience: "agent-platform-api",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
