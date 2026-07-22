using System.Reflection;
using Scalar.AspNetCore;

namespace AgentPlatform.Api.Configuration;

/// <summary>
/// Configures OpenAPI document generation (Microsoft.AspNetCore.OpenApi),
/// Swagger UI (Swashbuckle), and Scalar API reference UI.
/// </summary>
internal static class OpenApiConfiguration
{
    public static IServiceCollection AddOpenApiConfiguration(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            // Expose a Bearer (JWT) security scheme so the Scalar UI shows an Authorize button.
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();
                document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT bearer token. Use POST /api/dev/login (when Security:DevLoginEnabled=true) to mint a dev token for testing."
                };
                document.SecurityRequirements.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = new List<string>()
                });
                return Task.CompletedTask;
            });
        });

        services.AddSwaggerGen(options =>
        {
            // Register a Swagger document per API version.
            var apiVersions = new[] { "v1" };
            foreach (var version in apiVersions)
            {
                options.SwaggerDoc(version, new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "AgentPlatform API",
                    Version = version,
                    Description = "Multi-agent development platform API for managing agents, conversations, workflows, and model routing."
                });
            }

            // Bearer (JWT) security definition so Swagger UI shows the "Authorize" button.
            options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "Enter 'Bearer <your JWT token>'. Use POST /api/dev/login (when Security:DevLoginEnabled=true) to mint a dev token for testing."
            });
            options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);

            var appXmlFile = "AgentPlatform.Application.xml";
            var appXmlPath = Path.Combine(AppContext.BaseDirectory, appXmlFile);
            if (File.Exists(appXmlPath))
                options.IncludeXmlComments(appXmlPath);
        });

        return services;
    }
}
