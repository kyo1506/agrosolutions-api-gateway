using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgroSolutions.ApiGateway.HealthChecks;

/// <summary>
/// Health check customizado para verificar a saúde dos serviços downstream
/// </summary>
public class DownstreamServicesHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DownstreamServicesHealthCheck> _logger;

    public DownstreamServicesHealthCheck(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DownstreamServicesHealthCheck> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var services = new Dictionary<string, bool>();
        var isHealthy = true;

        // Lista de serviços para verificar (pode vir da configuração)
        var downstreamServices = new[]
        {
            ("Gestao API", "http://agrosolutions-gestao-api/health"),
            ("Ingestao API", "http://agrosolutions-ingestao-api/health"),
            ("Telemetria API", "http://agrosolutions-telemetria-api/health"),
        };

        foreach (var (name, url) in downstreamServices)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var response = await client.GetAsync(url, cancellationToken);
                var isServiceHealthy = response.IsSuccessStatusCode;

                services[name] = isServiceHealthy;

                if (!isServiceHealthy)
                {
                    isHealthy = false;
                    _logger.LogWarning("Service {ServiceName} is unhealthy", name);
                }
            }
            catch (Exception ex)
            {
                services[name] = false;
                isHealthy = false;
                _logger.LogError(ex, "Error checking health for {ServiceName}", name);
            }
        }

        var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;
        var description = isHealthy
            ? "All downstream services are healthy"
            : "One or more downstream services are unhealthy";

        return new HealthCheckResult(
            status,
            description,
            data: services.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
        );
    }
}
