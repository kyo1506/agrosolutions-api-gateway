# AgroSolutions - API Gateway

API Gateway desenvolvido com Ocelot para orquestração de microsserviços da plataforma AgroSolutions (Agricultura 4.0).

## 🌟 Visão Geral

O **AgroSolutions API Gateway** é o ponto de entrada unificado para todos os microsserviços da plataforma de Agricultura de Precisão. Implementado com Ocelot em .NET 10, segue princípios de Clean Architecture, SOLID e Clean Code.

### Principais Funcionalidades

- ✅ **Roteamento Inteligente**: Direcionamento de requisições para microsserviços específicos
- ✅ **Rate Limiting**: Proteção contra sobrecarga com políticas personalizadas (por rota via Ocelot + políticas ASP.NET Core)
- ✅ **Circuit Breaker**: Resiliência com padrão de Circuit Breaker via Polly (QoS)
- ✅ **Load Balancing**: Distribuição de carga via `RoundRobin` e `LeastConnection`
- ✅ **Caching**: Cache distribuído via CacheManager (TTL configurável por rota)
- ✅ **Autenticação JWT + Keycloak**: Validação centralizada com suporte a múltiplos issuers
- ✅ **Autorização por Scopes**: Controle de acesso baseado em scopes JWT via `RouteClaimsRequirement`
- ✅ **Correlation ID**: Rastreamento distribuído de requisições
- ✅ **Observabilidade**: Métricas Prometheus, logs estruturados (Serilog), tracing (OpenTelemetry)
- ✅ **Health Checks**: Monitoramento da saúde do gateway e dos serviços downstream

## 🏗️ Arquitetura

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ▼
┌──────────────────────────────────────────┐
│         API Gateway (Ocelot / .NET 10)   │
│                                          │
│  Pipeline de Middlewares (em ordem):     │
│  1. RequestLoggingMiddleware             │
│  2. ExceptionHandlingMiddleware          │
│  3. CorrelationIdMiddleware              │
│  4. Prometheus (UseMetricServer)         │
│  5. CORS                                 │
│  6. Authentication + Authorization       │
│  7. Ocelot (middleware terminal)         │
└──────────────┬───────────────────────────┘
               │ JWT (Keycloak)
               │ Scope validation
    ┌──────────┼──────────────┬──────────────┐
    ▼          ▼              ▼              ▼
┌──────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐
│ Identity │ │ Ingestion │ │Properties │ │  (other   │
│  API     │ │   API     │ │   API     │ │ services) │
└──────────┘ └───────────┘ └───────────┘ └───────────┘
       │
       ▼
┌──────────────┐
│   Keycloak   │
│  (JWT issuer)│
└──────────────┘
```

## 🚀 Tecnologias

| Tecnologia | Versão | Uso |
|---|---|---|
| **.NET** | 10.0 | Framework principal |
| **Ocelot** | 24.1.0 | Engine do API Gateway |
| **Ocelot.Cache.CacheManager** | 24.1.0 | Cache distribuído por rota |
| **Ocelot.Provider.Polly** | 24.1.0 | Circuit Breaker / QoS |
| **Microsoft.AspNetCore.Authentication.JwtBearer** | 10.0.2 | Validação JWT (Keycloak) |
| **Serilog.AspNetCore** | 10.0.0 | Logging estruturado |
| **prometheus-net.AspNetCore** | 8.2.1 | Métricas Prometheus |
| **OpenTelemetry** | 1.15.0 | Tracing distribuído |
| **xUnit + FluentAssertions + Moq** | - | Testes unitários |

## 📦 Estrutura do Projeto

```
agrosolutions-api-gateway/
├── src/
│   └── AgroSolutions.ApiGateway/
│       ├── Configuration/
│       │   ├── JwtAuthenticationExtensions.cs   # JWT + Keycloak (multi-issuer)
│       │   └── RateLimitingExtensions.cs        # 3 políticas ASP.NET Core
│       ├── Controllers/
│       │   └── InfoController.cs                # GET /api/info, /api/info/routes
│       ├── HealthChecks/
│       │   └── DownstreamServicesHealthCheck.cs # Health ativo dos downstream
│       ├── Middlewares/
│       │   ├── CorrelationIdMiddleware.cs        # Gera/propaga X-Correlation-Id
│       │   ├── ExceptionHandlingMiddleware.cs    # JSON de erro padronizado
│       │   └── RequestLoggingMiddleware.cs       # Log de todas as requisições
│       ├── ocelot.json                           # Configuração de rotas (produção)
│       ├── ocelot.Development.json              # Overrides de rota para dev
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Program.cs
├── tests/
│   └── AgroSolutions.ApiGateway.Tests/
│       └── Middlewares/
│           └── CorrelationIdMiddlewareTests.cs
├── k8s/
│   ├── namespace.yaml
│   ├── deployment.yaml                          # 3 réplicas, anti-affinity
│   ├── ingress.yaml
│   └── production/
│       ├── namespace.yaml
│       ├── deployment.yaml                      # Estratégia Recreate
│       ├── services.yaml
│       ├── configmaps.yaml                      # Ocelot config via ConfigMap
│       ├── infrastructure.yaml                  # ServiceAccount + IRSA (AWS)
│       ├── ingress-aws.yaml                     # ALB compartilhado
│       ├── hpa.yaml                             # min 1 / max 2 réplicas
│       ├── resource-configs.yaml                # Quotas, LimitRanges, PDB
│       └── observability.yaml
├── .github/
│   └── workflows/
│       ├── build.yml                            # CI: test + build + push ECR
│       └── deploy.yml                           # CD: deploy no EKS
├── Dockerfile                                   # Multi-stage Alpine, usuário não-root
├── docker-compose.yml
└── README.md
```

## 🔧 Configuração

### Rotas Configuradas (`ocelot.json`)

| Chave | Rota Upstream | Downstream | Métodos | Autenticação | Scope Requerido | Rate Limit |
|---|---|---|---|---|---|---|
| `identity-login` | `POST /identity/v1/login` | `agrosolutions-identity-api` | POST | Não | — | 30/min |
| `identity-register` | `POST /identity/v1/register` | `agrosolutions-identity-api` | POST | Não | — | 20/min |
| `identity-users-list` | `GET /identity/v1/users` | `agrosolutions-identity-api` | GET | JWT | `users:manage` | 100/min |
| `identity-user-get-by-id` | `GET /identity/v1/users/{id}` | `agrosolutions-identity-api` | GET | JWT | `users:read` | 100/min |
| `identity-user-update` | `PUT /identity/v1/users/{id}` | `agrosolutions-identity-api` | PUT | JWT | `users:manage` | 100/min |
| `identity-user-delete` | `DELETE /identity/v1/users/{id}` | `agrosolutions-identity-api` | DELETE | JWT | `users:manage` | 100/min |
| `identity-profile` | `/identity/v1/profile` | `agrosolutions-identity-api` | GET, PUT | JWT | `profiles:manage` | 100/min |
| `ingestao-sensor` | `POST /ingestao/sensor` | `agrosolutions-ingestion-api` | POST | Não | — | 5000/s (LeastConnection) |
| `properties-read` | `GET /properties/v1/*` | `agrosolutions-properties-api` | GET | JWT | `users:read` | 200/min |
| `properties-write` | `/properties/v1/*` | `agrosolutions-properties-api` | POST, PUT, DELETE | JWT | `users:manage` | 100/min |

### Matriz de Autorização por Scope

| Scope | Permissões |
|---|---|
| `users:read` | Leitura de usuários e profiles (`GET`) |
| `users:manage` | CRUD completo de usuários |
| `profiles:manage` | Leitura e edição do próprio perfil |

### Resiliência via QoS (Polly) — padrão por rota

| Parâmetro | Valor |
|---|---|
| `ExceptionsAllowedBeforeBreaking` | 3 (sensor: 10) |
| `DurationOfBreak` | 30.000 ms (sensor: 5.000 ms) |
| `TimeoutValue` | 10.000 ms (sensor: 3.000 ms) |

### Variáveis de Ambiente

```bash
# Ambiente
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80

# JWT / Keycloak
Jwt__Authority=http://keycloak:8080/realms/agrosolutions
Jwt__Audience=agrosolutions-api
Jwt__ExternalAuthority=http://keycloak-admin.agrosolutions.site/realms/agrosolutions  # opcional

# Rate Limiting
RateLimiting__EnableRateLimiting=true
RateLimiting__DefaultLimit=100
RateLimiting__DefaultPeriodInSeconds=60

# OpenTelemetry
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector-service:4317
OTEL_SERVICE_NAME=agrosolutions-api-gateway
```

## 🔒 Segurança e Autenticação

### Integração JWT + Keycloak

O gateway valida JWTs emitidos pelo Keycloak (realm `agrosolutions`) sem chamadas a endpoints de introspection. O fluxo é:

1. Cliente autentica via `POST /identity/v1/login` → Identity Service → Keycloak emite JWT
2. Cliente envia requisição com `Authorization: Bearer <token>`
3. Gateway valida JWT (assinatura, issuer, audience, expiração via OIDC discovery)
4. Ocelot verifica `scope` do token contra `RouteClaimsRequirement` da rota
5. Requisição autorizada é encaminhada ao serviço downstream

**Configuração em `appsettings.json`:**

```json
{
  "Jwt": {
    "Authority": "http://keycloak:8080/realms/agrosolutions",
    "Audience": "agrosolutions-api",
    "ExternalAuthority": "http://keycloak-admin.agrosolutions.site/realms/agrosolutions"
  }
}
```

> `ExternalAuthority` é opcional e permite que tokens emitidos pelo Keycloak acessível externamente (ex.: via Ingress) também sejam válidos.

### Endpoints Anônimos

Rotas sem `AuthenticationOptions` em `ocelot.json`:

- `POST /identity/v1/login`
- `POST /identity/v1/register`
- `POST /ingestao/sensor`
- `GET /health`, `GET /health/ready`, `GET /health/live`
- `GET /metrics`

### Outras Proteções

- **CORS**: Política `AllowAll` configurável em `Program.cs`
- **Container Security**: Imagem Alpine com usuário não-root (`appuser:appgroup`, UID/GID 1001)
- **Secrets Management**: Kubernetes Secrets + IRSA (AWS IAM Roles for Service Accounts)
- **ClockSkew = Zero**: Validação estrita de expiração do token

## 🐳 Docker

### Build da Imagem

```bash
docker build -t agrosolutions/api-gateway:latest .
```

### Executar com Docker Compose

```bash
docker-compose up -d
```

O gateway estará disponível em `http://localhost:5000`.

> O `docker-compose.yml` conecta ao network externo `agrosolutions-network` (compartilhado com os demais serviços do ecossistema).

## ☸️ Kubernetes

### Deploy (desenvolvimento)

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/ingress.yaml
```

### Deploy (produção — AWS EKS)

```bash
kubectl apply -f k8s/production/namespace.yaml
kubectl apply -f k8s/production/infrastructure.yaml   # ServiceAccount + IRSA
kubectl apply -f k8s/production/resource-configs.yaml # Quotas, LimitRanges, PDB
kubectl apply -f k8s/production/configmaps.yaml
kubectl apply -f k8s/production/services.yaml
kubectl apply -f k8s/production/deployment.yaml
kubectl apply -f k8s/production/ingress-aws.yaml      # ALB compartilhado
kubectl apply -f k8s/production/hpa.yaml              # HPA: 1–2 réplicas
```

### Verificar Status

```bash
# Produção
kubectl get pods -n agrosolutions-gateway
kubectl get svc -n agrosolutions-gateway
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Desenvolvimento
kubectl get pods -n agrosolutions
kubectl logs -f deployment/agrosolutions-api-gateway -n agrosolutions
```

### HPA (produção)

| Parâmetro | Valor |
|---|---|
| `minReplicas` | 1 |
| `maxReplicas` | 2 |
| CPU target | 80% |
| Memory target | 80% |
| Scale-down window | 300 s |

## 📊 Observabilidade

### Endpoints de Monitoramento

| Endpoint | Descrição |
|---|---|
| `GET /health` | Status geral |
| `GET /health/ready` | Kubernetes readiness probe |
| `GET /health/live` | Kubernetes liveness probe |
| `GET /metrics` | Scraping Prometheus |
| `GET /api/info` | Versão e ambiente do gateway |
| `GET /api/info/routes` | Rotas configuradas no Ocelot |

### Stack de Observabilidade

- **Serilog**: logs estruturados no console + arquivo rotativo diário (`logs/gateway-.log`)  
  Enrichers ativos: `FromLogContext`, `WithMachineName`, `WithThreadId`
- **Prometheus**: métricas expostas em `/metrics` via `prometheus-net` (`UseMetricServer` + `UseHttpMetrics`)
- **OpenTelemetry**: traces enviados via OTLP gRPC ao `otel-collector-service` (namespace `agrosolutions-observability`)  
  Instrumentações: ASP.NET Core, HttpClient, Runtime metrics

### Métricas Disponíveis

- Requisições HTTP (total, latência por percentil, taxa de erro)
- Métricas de runtime .NET (GC, thread pool, heap)
- Circuit breaker (aberto/fechado) via Polly
- Rate limiting (requests rejeitados por política)

## 🧪 Testes

```bash
dotnet restore
dotnet build
dotnet test
```

Os testes utilizam **xUnit + FluentAssertions + Moq**. A classe `Program` é exposta como `partial` para suportar `WebApplicationFactory` em testes de integração.

Testes implementados:
- `CorrelationIdMiddlewareTests`: verifica geração e propagação do `X-Correlation-Id`

## 🚦 Rate Limiting

Duas camadas de limitação:

### Camada 1 — Ocelot (por rota, em `ocelot.json`)

Configurado com `RateLimitOptions` em cada rota. Suporte a `ClientWhitelist`.

| Rota | Período | Limite |
|---|---|---|
| `/identity/v1/login` | 1 min | 30 |
| `/identity/v1/register` | 1 min | 20 |
| `/identity/v1/users*` | 1 min | 100 |
| `/ingestao/sensor` | 1 s | 5000 |
| `/properties/v1/*` | 1 min | 100–200 |

### Camada 2 — ASP.NET Core (`RateLimitingExtensions.cs`)

Ativado quando `RateLimiting__EnableRateLimiting=true`.

| Política | Algoritmo | Chave de Partição | Limite |
|---|---|---|---|
| `GlobalLimiter` | Fixed Window | IP do cliente | 100 req/60s |
| `IngestPolicy` | Sliding Window | IP do cliente | 1000 req/60s (6 segmentos) |
| `ReadPolicy` | Token Bucket | Usuário autenticado | 500 tokens (100 tokens/60s) |

## 📝 Logs

Os logs são estruturados (JSON) e incluem:

- **Correlation ID**: propagado pelo `CorrelationIdMiddleware`
- **MachineName** e **ThreadId**: via enrichers Serilog
- **Timestamp**: UTC
- **Level**: Debug / Information / Warning / Error

Exemplo de log de requisição:

```json
{
  "Timestamp": "2026-02-23T10:30:00.000Z",
  "Level": "Information",
  "MessageTemplate": "Incoming Request: {Method} {Path}",
  "Properties": {
    "Method": "GET",
    "Path": "/properties/v1/farms",
    "CorrelationId": "abc123-def456",
    "MachineName": "pod-api-gateway-abc",
    "ThreadId": 12,
    "SourceContext": "AgroSolutions.ApiGateway.Middlewares.RequestLoggingMiddleware"
  }
}
```

## 🎯 Princípios Aplicados

### SOLID

- **Single Responsibility**: Cada middleware tem uma única responsabilidade
- **Open/Closed**: Extensível via configuração e novos middlewares sem alterar código existente
- **Dependency Inversion**: Injeção de dependências via DI container do ASP.NET Core

### Clean Code

- **Primary Constructors** (C# 12): todos os middlewares usam primary constructor
- Nomes descritivos e funções focadas
- Tratamento de erros centralizado e padronizado (`ExceptionHandlingMiddleware`)

### Clean Architecture

- Lógica de configuração isolada em `Configuration/`
- Middlewares independentes do motor de roteamento
- Testabilidade garantida pela exposição de `Program` como `partial class`

## 🤝 Contribuindo

1. Fork o projeto
2. Crie uma branch para sua feature (`git checkout -b feature/AmazingFeature`)
3. Commit suas mudanças (`git commit -m 'Add some AmazingFeature'`)
4. Push para a branch (`git push origin feature/AmazingFeature`)
5. Abra um Pull Request

## 📄 Licença

Este projeto faz parte do Hackathon AgroSolutions - Agricultura 4.0

## 👥 Equipe

Desenvolvido pela equipe AgroSolutions

---

**AgroSolutions** - Transformando a agricultura através da tecnologia 🌱
