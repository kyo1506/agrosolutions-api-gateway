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
                        ValidIssuer = authority,
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
                            var scopes = context.Principal?.FindFirst("scope")?.Value;
                            logger.LogInformation(
                                "Token validated for user {UserId} with scopes: {Scopes}",
                                userId,
                                scopes
                            );
                            return Task.CompletedTask;
                        },
                    };
                }
            );

        return services;
    }
}
