using System.Threading.RateLimiting;

namespace AgroSolutions.ApiGateway.Configuration;

/// <summary>
/// Extensões para configuração de Rate Limiting
/// Implementa diferentes estratégias de limitação de taxa
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var rateLimitingConfig = configuration.GetSection("RateLimiting");
        var enableRateLimiting = rateLimitingConfig.GetValue<bool>("EnableRateLimiting");

        if (!enableRateLimiting)
        {
            return services;
        }

        services.AddRateLimiter(options =>
        {
            // Política padrão - Fixed Window
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitingConfig.GetValue<int>("DefaultLimit", 100),
                        Window = TimeSpan.FromSeconds(
                            rateLimitingConfig.GetValue<int>("DefaultPeriodInSeconds", 60)
                        ),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2,
                    }
                );
            });

            // Política para rotas de ingestão - Sliding Window (maior throughput)
            options.AddPolicy(
                "IngestPolicy",
                context =>
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                        factory: partition => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 1000,
                            Window = TimeSpan.FromSeconds(60),
                            SegmentsPerWindow = 6,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 10,
                        }
                    );
                }
            );

            // Política para APIs de leitura - Token Bucket (mais flexível)
            options.AddPolicy(
                "ReadPolicy",
                context =>
                {
                    return RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: context.User.Identity?.Name ?? "anonymous",
                        factory: partition => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 500,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(60),
                            TokensPerPeriod = 100,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 5,
                        }
                    );
                }
            );

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var logger = context.HttpContext.RequestServices.GetRequiredService<
                    ILogger<Program>
                >();

                logger.LogWarning(
                    "Rate limit exceeded for {IpAddress} on {Path}",
                    context.HttpContext.Connection.RemoteIpAddress,
                    context.HttpContext.Request.Path
                );

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "Rate limit exceeded",
                        message = "Too many requests. Please try again later.",
                        retryAfter = context.Lease.TryGetMetadata(
                            MetadataName.RetryAfter,
                            out var retryAfter
                        )
                            ? retryAfter.TotalSeconds
                            : null,
                    },
                    cancellationToken: cancellationToken
                );
            };
        });

        return services;
    }
}
