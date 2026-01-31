# AgroSolutions API Gateway - AI Agent Instructions

## Project Context
This is an **Ocelot-based API Gateway** for the AgroSolutions precision agriculture platform (Agricultura 4.0), built with **.NET 10**. It routes traffic to downstream microservices and integrates with **Keycloak (via Identity Service)** for JWT authentication and scope-based authorization.

## Architecture Overview
- **Routing Engine**: Ocelot handles all request routing via [ocelot.json](src/AgroSolutions.ApiGateway/ocelot.json) configuration
- **Authentication**: JWT Bearer tokens issued by Keycloak (realm: `agrosolutions`)
- **Authorization**: Scope-based access control configured in Ocelot routes (`AuthenticationOptions` + `RouteClaimsRequirement`)
- **Middleware Pipeline Order** (critical): `RequestLogging` → `ExceptionHandling` → `CorrelationId` → Ocelot
- **Observability Stack**: Serilog (structured logs) + Prometheus (metrics) + OpenTelemetry (tracing)
- **Resilience**: QoS with Circuit Breaker, Rate Limiting, Load Balancing
- **Deployment**: Docker + Kubernetes (3 replicas with anti-affinity)

## Key Patterns & Conventions

### 1. Keycloak + Identity Service Integration

**Identity Service** (`agrosolutions-service-identity`) handles:
- User authentication (`/v1/login`, `/v1/register`)
- JWT token issuance from Keycloak realm `agrosolutions`
- User management (CRUD operations with role assignment)

**Keycloak Token Structure**:
```json
{
  "sub": "user-guid",
  "preferred_username": "john.doe",
  "email": "john@example.com",
  "scope": "users:read profiles:manage",
  "realm_access": {"roles": ["admin", "user"]}
}
```

**3 Core Scopes** (defined in Identity Service):
- `users:read` - Read user information
- `users:manage` - Full user management (CRUD)
- `profiles:manage` - Manage own profile

**API Gateway validates JWT and enforces scope-based access** - no calls to `/validate-token` or `/validate-permission` endpoints.

### 2. Ocelot Route Configuration with Authorization

Each protected route in `ocelot.json` must have:
```json
{
  "AuthenticationOptions": {
    "AuthenticationProviderKey": "Bearer"
  },
  "RouteClaimsRequirement": {
    "scope": "users:read"  // Or comma-separated: "users:read,profiles:manage"
  }
}
```

**Anonymous Routes** (no AuthenticationOptions):
- `/identity/v1/login`
- `/identity/v1/register`
- `/health`, `/health/ready`, `/health/live`
- `/metrics` (Prometheus)

### 3. JWT Configuration in API Gateway

In [JwtAuthenticationExtensions.cs](src/AgroSolutions.ApiGateway/Configuration/JwtAuthenticationExtensions.cs):
```csharp
options.Authority = "http://keycloak:8080/realms/agrosolutions"; // Keycloak realm
options.Audience = "agrosolutions-api"; // Client ID
options.RequireHttpsMetadata = false; // Only for dev
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ClockSkew = TimeSpan.Zero
};
```

### 4. Configuration-First Routing
All routing logic lives in `ocelot.json`. Each route defines:
- **DownstreamHostAndPorts**: Service hostnames (e.g., `agrosolutions-gestao-api`, `agrosolutions-identity-api`)
- **AuthenticationOptions**: JWT validation requirement
- **RouteClaimsRequirement**: Scope-based authorization
- **RateLimitOptions**: Per-route limits (Gestão: 100/min, Ingestão: 1000/min, Identity: 200/min)
- **QoSOptions**: Circuit breaker thresholds and timeouts
- **LoadBalancerOptions**: `RoundRobin` (default) or `LeastConnection` (Ingestão)
- **FileCacheOptions**: Only Telemetria route has caching (30s TTL)

Example protected route:
```json
{
  "DownstreamPathTemplate": "/api/gestao/{everything}",
  "UpstreamPathTemplate": "/gestao/{everything}",
  "UpstreamHttpMethod": ["GET", "POST", "PUT", "DELETE"],
  "AuthenticationOptions": {
    "AuthenticationProviderKey": "Bearer"
  },
  "RouteClaimsRequirement": {
    "scope": "users:read"
  }
}
```

Example anonymous route (Identity Service login/register):
```json
{
  "DownstreamPathTemplate": "/v1/{everything}",
  "UpstreamPathTemplate": "/identity/v1/{everything}",
  "UpstreamHttpMethod": ["POST"],
  "RateLimitOptions": {
    "Limit": 50,
    "Period": "1m"
  }
}
```

### 5. Extension Methods for Configuration
Configuration logic is isolated in `Configuration/` folder:
- **JwtAuthenticationExtensions**: Configures JWT Bearer authentication with Keycloak Authority and Audience
- **RateLimitingExtensions**: Defines 3 policies (GlobalLimiter, IngestPolicy, ReadPolicy) using .NET rate limiters

### 6. Custom Middlewares (All Primary Constructors)
Located in `Middlewares/` - use **C# 12 primary constructor syntax**:
```csharp
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
```
- **CorrelationIdMiddleware**: Generates/propagates `X-Correlation-Id` header, adds to HttpContext.Items
- **ExceptionHandlingMiddleware**: Returns standardized JSON error with correlation ID, hides stack traces in production
- **RequestLoggingMiddleware**: Logs all requests with timing

### 7. Observability Integration
In [Program.cs](src/AgroSolutions.ApiGateway/Program.cs):
- Serilog writes to console + rolling daily files (`logs/gateway-.log`)
- Prometheus exposed at `/metrics` via `app.UseMetricServer()` + `app.MapPrometheusScrapingEndpoint()`
- OpenTelemetry configured with ASP.NET Core + HttpClient instrumentation
- Health checks at `/health`, `/health/ready`, `/health/live`

## Development Workflows

### Build & Run Locally
```bash
dotnet restore
dotnet run --project src/AgroSolutions.ApiGateway
# Gateway runs on http://localhost:5000 (see docker-compose.yml)
```

### Docker Development
```bash
docker-compose up --build
# Access at http://localhost:5000
# Logs mounted to ./logs/ directory
```

### Testing
Tests use **xUnit + FluentAssertions + Moq**. See [CorrelationIdMiddlewareTests.cs](tests/AgroSolutions.ApiGateway.Tests/Middlewares/CorrelationIdMiddlewareTests.cs) for middleware testing pattern:
```bash
dotnet test
```

### Kubernetes Deployment
```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/ingress.yaml
```
Deployment uses 3 replicas with pod anti-affinity (spread across nodes).

## Critical Technical Details

### JWT + Keycloak Integration Flow
1. **User authenticates** → Identity Service `/v1/login` → Keycloak issues JWT token
2. **Client sends request** → API Gateway with `Authorization: Bearer <token>` header
3. **API Gateway validates JWT**:
   - Verifies signature using Keycloak's public key (fetched from `/.well-known/openid-configuration`)
   - Validates `iss` (issuer), `aud` (audience), `exp` (expiration)
   - Extracts claims: `sub`, `scope`, `realm_access.roles`
4. **Ocelot checks RouteClaimsRequirement**:
   - Compares required scopes in route config with token's `scope` claim
   - If match → forwards request to downstream service
   - If no match → returns 403 Forbidden
5. **Downstream service receives** authenticated request with propagated headers

### JWT Configuration Requirements
Settings in [appsettings.json](src/AgroSolutions.ApiGateway/appsettings.json):
```json
{
  "Jwt": {
    "Authority": "http://keycloak:8080/realms/agrosolutions",
    "Audience": "agrosolutions-api",
    "MetadataAddress": "http://keycloak:8080/realms/agrosolutions/.well-known/openid-configuration"
  }
}
```

**Important**: 
- `Authority` must match Keycloak realm URL exactly
- `Audience` must match the Client ID configured in Keycloak
- For production, set `RequireHttpsMetadata = true`

### Scope-Based Authorization Matrix

| Route Pattern | Required Scope | Anonymous |
|--------------|----------------|-----------|
| `/identity/v1/login` | None | ✅ |
| `/identity/v1/register` | None | ✅ |
| `/gestao/*` (GET) | `users:read` | ❌ |
| `/gestao/*` (POST/PUT/DELETE) | `users:manage` | ❌ |
| `/ingestao/*` | `users:manage` | ❌ |
| `/telemetria/*` | `users:read` | ❌ |
| `/alertas/*` | `users:manage` | ❌ |
| `/health`, `/metrics` | None | ✅ |

**Multiple Scopes**: Use comma-separated values in `RouteClaimsRequirement`:
```json
"RouteClaimsRequirement": {
  "scope": "users:read,users:manage"  // User must have ANY of these scopes
}
```

### Rate Limiting Strategy
- **Ocelot's Built-in Limiter** (in ocelot.json): Applied per-route with `ClientWhitelist` support
  - Identity Service: 200/min (includes login/register)
  - Gestão: 100/min
  - Ingestão: 1000/min
- **ASP.NET Core Rate Limiter** (in RateLimitingExtensions): 
  - GlobalLimiter = Fixed Window by IP
  - IngestPolicy = Sliding Window (smoother for high-throughput)
  - ReadPolicy = Token Bucket by authenticated user

### Health Checks
All health endpoints return 200 OK when healthy:
- `/health` - Overall status
- `/health/ready` - Kubernetes readiness probe
- `/health/live` - Kubernetes liveness probe

### Correlation ID Flow
1. Request arrives → CorrelationIdMiddleware extracts or generates ID
2. ID stored in `HttpContext.Items["X-Correlation-Id"]`
3. Returned in response header `X-Correlation-Id`
4. Referenced in error responses and logs

## Common Tasks

### Adding Identity Service Route (with Authentication)
1. Edit [ocelot.json](src/AgroSolutions.ApiGateway/ocelot.json)
2. Add route to Identity Service at `agrosolutions-identity-api:80`:
```json
{
  "DownstreamPathTemplate": "/v1/{everything}",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [{"Host": "agrosolutions-identity-api", "Port": 80}],
  "UpstreamPathTemplate": "/identity/v1/{everything}",
  "UpstreamHttpMethod": ["GET", "POST", "PUT", "DELETE"],
  "AuthenticationOptions": {
    "AuthenticationProviderKey": "Bearer"
  },
  "RouteClaimsRequirement": {
    "scope": "users:read,users:manage"  // Adjust based on endpoint
  },
  "RateLimitOptions": {
    "EnableRateLimiting": true,
    "Period": "1m",
    "Limit": 200
  }
}
```

3. For **anonymous endpoints** (login/register), create separate routes without `AuthenticationOptions`:
```json
{
  "DownstreamPathTemplate": "/v1/{action}",
  "UpstreamPathTemplate": "/identity/v1/{action}",
  "UpstreamHttpMethod": ["POST"],
  "RateLimitOptions": {
    "EnableRateLimiting": true,
    "Period": "1m",
    "Limit": 50
  }
}
```

### Configuring JWT Authentication
Update [appsettings.json](src/AgroSolutions.ApiGateway/appsettings.json):
```json
{
  "Jwt": {
    "Authority": "http://keycloak:8080/realms/agrosolutions",
    "Audience": "agrosolutions-api"
  }
}
```

In [JwtAuthenticationExtensions.cs](src/AgroSolutions.ApiGateway/Configuration/JwtAuthenticationExtensions.cs):
- `Authority` points to Keycloak realm
- `Audience` must match Client ID in Keycloak
- `RequireHttpsMetadata = false` only for dev (set `true` in production)
- `TokenValidationParameters.ClockSkew = TimeSpan.Zero` for strict expiration validation

### Adding a New Protected Route
1. Edit [ocelot.json](src/AgroSolutions.ApiGateway/ocelot.json)
2. Add `AuthenticationOptions` with `AuthenticationProviderKey: "Bearer"`
3. Add `RouteClaimsRequirement` with required scope(s)
4. Configure RateLimitOptions, QoSOptions (Circuit Breaker), LoadBalancerOptions
5. Restart gateway

### Adding a New Middleware
1. Create in `Middlewares/` using primary constructor
2. Add `app.UseMiddleware<YourMiddleware>()` in [Program.cs](src/AgroSolutions.ApiGateway/Program.cs) **before** `app.UseOcelot()`
3. Middleware order matters - logging first, exception handling early

### Changing Rate Limits
- **Per-route limits**: Update `RateLimitOptions` in ocelot.json
- **Global/policy limits**: Update `RateLimitingExtensions.cs` and appsettings.json `RateLimiting` section

### Debugging Authentication Issues
1. Check Keycloak is running and accessible at configured Authority URL
2. Verify JWT token contains required scopes: decode at [jwt.io](https://jwt.io)
3. Check Serilog logs for JWT validation failures in `logs/` directory
4. Confirm `Audience` matches Keycloak Client ID
5. Test with curl:
```bash
# Get token from Identity Service
TOKEN=$(curl -X POST http://localhost:5000/identity/v1/login \
  -H "Content-Type: application/json" \
  -d '{"username":"user","password":"pass"}' | jq -r '.data.accessToken')

# Test protected route
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/gestao/something
```

### Debugging Routing Issues
1. Check Serilog logs in `logs/` directory or console output
2. Verify ocelot.json syntax (invalid JSON breaks startup)
3. Confirm downstream service hostnames resolve (use k8s service names or docker-compose service names)
4. Test health endpoints of downstream services

## Technology Stack
- **.NET 10** (net10.0 target framework)
- **Ocelot 24.1.0** - API Gateway engine
- **Serilog** - Structured logging with enrichers
- **Prometheus-net** - Metrics collection
- **OpenTelemetry 1.15.0** - Distributed tracing
- **JWT Bearer Authentication** - Token validation
- **xUnit + FluentAssertions + Moq** - Testing

## File References
- Main entry: [Program.cs](src/AgroSolutions.ApiGateway/Program.cs)
- Routing config: [ocelot.json](src/AgroSolutions.ApiGateway/ocelot.json)
- K8s deployment: [k8s/deployment.yaml](k8s/deployment.yaml)
- Docker setup: [docker-compose.yml](docker-compose.yml)
