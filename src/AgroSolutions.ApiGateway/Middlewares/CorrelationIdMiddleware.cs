namespace AgroSolutions.ApiGateway.Middlewares;

/// <summary>
/// Middleware para adicionar ou propagar Correlation ID nas requisições
/// Seguindo o padrão de rastreamento distribuído
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly ILogger<CorrelationIdMiddleware> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // Adicionar ao contexto para uso posterior
        context.Items[CorrelationIdHeader] = correlationId;

        // Adicionar ao response header
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
            {
                context.Response.Headers.Append(CorrelationIdHeader, correlationId);
            }
            return Task.CompletedTask;
        });

        using (
            _logger.BeginScope(
                new Dictionary<string, object> { [CorrelationIdHeader] = correlationId }
            )
        )
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (
            context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId)
            && !string.IsNullOrWhiteSpace(correlationId)
        )
        {
            return correlationId.ToString();
        }

        return Guid.NewGuid().ToString();
    }
}
