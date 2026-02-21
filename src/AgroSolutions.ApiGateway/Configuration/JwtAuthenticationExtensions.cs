using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AgroSolutions.ApiGateway.Configuration;

/// <summary>
/// Extensões para configuração de autenticação JWT com Keycloak
/// </summary>
public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var jwtSettings = configuration.GetSection("Jwt");
        var authority =
            jwtSettings["Authority"]
            ?? throw new InvalidOperationException("JWT Authority not configured");
        var audience =
            jwtSettings["Audience"]
            ?? throw new InvalidOperationException("JWT Audience not configured");

        // Issuers válidos: interno (keycloak-service FQDN) + externo (via Ingress/ALB)
        var externalAuthority = jwtSettings["ExternalAuthority"];
        var validIssuers = new List<string> { authority };
        if (!string.IsNullOrEmpty(externalAuthority))
            validIssuers.Add(externalAuthority);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(
                "Bearer",
                options =>
                {
                    // URL do Keycloak realm
                    options.Authority = authority;
                    // Client ID configurado no Keycloak
                    options.Audience = audience;
                    // Apenas false em desenvolvimento
                    options.RequireHttpsMetadata = false;

                    // Configuração do MetadataAddress para buscar as chaves públicas do Keycloak
                    options.MetadataAddress = $"{authority}/.well-known/openid-configuration";

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidIssuers = validIssuers,
                        ValidAudience = audience,
                        // Remove tolerância de tempo - validação estrita
                        ClockSkew = TimeSpan.Zero,
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<
                                ILogger<Program>
                            >();
                            logger.LogError(
                                context.Exception,
                                "JWT authentication failed: {Message}",
                                context.Exception.Message
                            );
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<
                                ILogger<Program>
                            >();
                            var userId = context.Principal?.FindFirst("sub")?.Value;
                            var scopeClaim = context.Principal?.FindFirst("scope")?.Value;

                            logger.LogInformation(
                                "Token validated for user {UserId} with scopes: {Scopes}",
                                userId,
                                scopeClaim
                            );

                            // Ocelot RouteClaimsRequirement compara claim values por vírgula.
                            // Keycloak emite scopes separados por espaço em uma única claim.
                            // Solução: dividir a claim de scope por espaço e adicionar cada
                            // scope como uma claim individual, compatível com o Ocelot.
                            if (!string.IsNullOrEmpty(scopeClaim) && context.Principal != null)
                            {
                                var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                var identity = context.Principal.Identity as ClaimsIdentity;
                                if (identity != null)
                                {
                                    foreach (var scope in scopes)
                                    {
                                        if (!identity.HasClaim("scope", scope))
                                            identity.AddClaim(new Claim("scope", scope));
                                    }
                                }
                            }

                            return Task.CompletedTask;
                        },
                    };
                }
            );

        return services;
    }
}
