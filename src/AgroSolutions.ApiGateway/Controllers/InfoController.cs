using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace AgroSolutions.ApiGateway.Controllers;

/// <summary>
/// Controller para informações sobre o API Gateway
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InfoController : ControllerBase
{
    private readonly ILogger<InfoController> _logger;

    public InfoController(ILogger<InfoController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Retorna informações sobre a versão e configuração do gateway
    /// </summary>
    [HttpGet]
    public IActionResult GetInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var info = new
        {
            serviceName = "AgroSolutions API Gateway",
            version,
            environment,
            uptime = GetUptime(),
            timestamp = DateTime.UtcNow
        };

        _logger.LogInformation("Info endpoint accessed");

        return Ok(info);
    }

    /// <summary>
    /// Retorna as rotas configuradas no gateway
    /// </summary>
    [HttpGet("routes")]
    public IActionResult GetRoutes()
    {
        var routes = new
        {
            routes = new[]
            {
                new { path = "/gestao/*", service = "Gestão API", methods = new[] { "GET", "POST", "PUT", "DELETE" } },
                new { path = "/ingestao/*", service = "Ingestão API", methods = new[] { "POST" } },
                new { path = "/telemetria/*", service = "Telemetria API", methods = new[] { "GET" } },
                new { path = "/alertas/*", service = "Alertas API", methods = new[] { "GET", "POST", "PUT" } },
                new { path = "/dashboard/*", service = "Dashboard API", methods = new[] { "GET" } }
            }
        };

        return Ok(routes);
    }

    private static string GetUptime()
    {
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
    }
}
