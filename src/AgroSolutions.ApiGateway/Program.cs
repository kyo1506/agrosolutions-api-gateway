using AgroSolutions.ApiGateway.Configuration;
using AgroSolutions.ApiGateway.Middlewares;
using Ocelot.Cache.CacheManager;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console()
    .WriteTo.File("logs/gateway-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Configuração do Ocelot
builder
    .Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile(
        $"ocelot.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: true
    )
    .AddEnvironmentVariables();

// Adicionar serviços ao contêiner
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    );
});

// Configurar Health Checks
builder.Services.AddHealthChecks();

// Configurar OpenTelemetry
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgroSolutions.ApiGateway"))
    .WithMetrics(metrics =>
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter()
    )
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

// Configurar Ocelot
builder
    .Services.AddOcelot(builder.Configuration)
    .AddCacheManager(x =>
    {
        x.WithDictionaryHandle();
    });

// Configurar autenticação JWT (se necessário)
builder.Services.AddJwtAuthentication(builder.Configuration);

// Configurar Rate Limiting customizado
builder.Services.AddRateLimiting(builder.Configuration);

var app = builder.Build();

// Configurar pipeline de requisição HTTP
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middlewares customizados
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

// Métricas do Prometheus
app.UseMetricServer();
app.UseHttpMetrics();

app.UseCors("AllowAll");

app.UseRouting();

// Health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// Ocelot Middleware
await app.UseOcelot();

app.Run();

// Tornar a classe Program acessível para testes
public partial class Program { }
