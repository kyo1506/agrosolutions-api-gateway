using System.Diagnostics;

namespace AgroSolutions.ApiGateway.Middlewares;

/// <summary>
/// Middleware para logging estruturado de requisições e respostas
/// Inclui métricas de performance e rastreamento
/// </summary>
public class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger
)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly ILogger<RequestLoggingMiddleware> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = context.Request;

        // Log inicial da requisição
        _logger.LogInformation(
            "Incoming Request: {Method} {Path} {QueryString} from {RemoteIp}",
            request.Method,
            request.Path,
            request.QueryString,
            context.Connection.RemoteIpAddress
        );

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var response = context.Response;
            var logLevel =
                response.StatusCode >= 500 ? LogLevel.Error
                : response.StatusCode >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                logLevel,
                "Outgoing Response: {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms",
                request.Method,
                request.Path,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds
            );
        }
    }
}
