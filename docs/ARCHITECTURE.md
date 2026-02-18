# 🏗️ Arquitetura do AgroSolutions API Gateway

## 📋 Visão Geral

O **AgroSolutions API Gateway** é construído com **Ocelot** (.NET 10) e serve como **único ponto de entrada público** para todos os microsserviços da plataforma AgroSolutions.

### Tecnologias Principais

- **Ocelot 24.1.0** - API Gateway engine (routing, rate limiting, QoS)
- **ASP.NET Core 10** - Authentication, Authorization, Middlewares
- **Serilog** - Structured logging
- **Prometheus** - Metrics collection
- **OpenTelemetry** - Distributed tracing
- **AWS Application Load Balancer** - Ingress controller (TLS termination, health checks)

---

## 🌐 Arquitetura de Rede

### Fluxo de Requisições

```
┌─────────────┐
│   Internet  │
└──────┬──────┘
       │
       ▼
┌─────────────────────────────────────┐
│  AWS Application Load Balancer      │
│  (TLS/SSL Termination)              │
│  api.agrosolutions.com              │
└──────┬──────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────┐
│  API Gateway (Ocelot) - Namespace:  │
│  agrosolutions-gateway              │
│  ┌───────────────────────────────┐  │
│  │ Port 80 (HTTP)                │  │
│  │ Replicas: 3-10 (HPA)          │  │
│  │ Service: ClusterIP            │  │
│  └───────────────────────────────┘  │
└──────┬──────────────────────────────┘
       │
       ├─── /identity/v1/*  ──────────────────────────┐
       │                                              ▼
       │                        ┌─────────────────────────────────────┐
       │                        │ Identity Service - Namespace:       │
       │                        │ agrosolutions-identity              │
       │                        │ ┌─────────────────────────────────┐ │
       │                        │ │ identity-api-service (ClusterIP)│ │
       │                        │ │ Port: 80                        │ │
       │                        │ └──────┬──────────────────────────┘ │
       │                        │        │                            │
       │                        │        ▼                            │
       │                        │ ┌─────────────────────────────────┐ │
       │                        │ │ Keycloak (ClusterIP)            │ │
       │                        │ │ Port: 8080                      │ │
       │                        │ │ keycloak-service                │ │
       │                        │ └──────┬──────────────────────────┘ │
       │                        │        │                            │
       │                        │        ▼                            │
       │                        │ ┌─────────────────────────────────┐ │
       │                        │ │ PostgreSQL DB                   │ │
       │                        │ │ Port: 5432                      │ │
       │                        │ └─────────────────────────────────┘ │
       │                        └─────────────────────────────────────┘
       │
       ├─── /gestao/*  ───────────────────────────────┐
       │                                              ▼
       │                        ┌─────────────────────────────────────┐
       │                        │ Gestão Service (ClusterIP)          │
       │                        │ Port: 80                            │
       │                        └─────────────────────────────────────┘
       │
       ├─── /ingestao/*  ─────────────────────────────┐
       │                                              ▼
       │                        ┌─────────────────────────────────────┐
       │                        │ Ingestão Service (ClusterIP)        │
       │                        │ Port: 80                            │
       │                        └─────────────────────────────────────┘
       │
       └─── /telemetria/*  ───────────────────────────┐
                                                      ▼
                        ┌─────────────────────────────────────┐
                        │ Telemetria Service (ClusterIP)      │
                        │ Port: 80                            │
                        └─────────────────────────────────────┘
```

---

## 🔑 Pontos Importantes da Arquitetura

### 1. API Gateway como Único Ponto de Entrada

- ✅ **Serviço Exposto**: Apenas o API Gateway tem acesso público via AWS ALB
- ✅ **Downstream Services**: Todos são **ClusterIP** (acessíveis apenas internamente)
- ✅ **Comunicação Inter-Namespace**: Via DNS interno Kubernetes
  - Exemplo: `identity-api-service.agrosolutions-identity:80`

### 2. Responsabilidades do API Gateway

#### Roteamento (Ocelot)
- Path-based routing para downstream services
- Load balancing (RoundRobin, LeastConnection)
- Service discovery via Kubernetes DNS

```json
"DownstreamHostAndPorts": [
  {
    "Host": "identity-api-service.agrosolutions-identity",
    "Port": 80
  }
],
"UpstreamPathTemplate": "/identity/v1/{everything}"
```

#### Autenticação JWT (ASP.NET Core)
- Valida tokens JWT do Keycloak
- Extrai claims: `sub`, `scope`, `realm_access.roles`
- Configuração: `Jwt__Authority`, `Jwt__Audience`

```csharp
Jwt__Authority = "http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions"
Jwt__Audience = "agrosolutions-api"
```

#### Autorização (Ocelot + Claims)
- Scope-based authorization via `RouteClaimsRequirement`
- Valida scopes no token antes de rotear

```json
"RouteClaimsRequirement": {
  "scope": "users:read,users:manage"
}
```

#### Rate Limiting (Ocelot + ASP.NET Core)
- **Ocelot**: Rate limits por rota (configurado em ocelot.json)
  - Identity: 200/min
  - Gestão: 100/min
  - Ingestão: 1000/min
- **ASP.NET Core**: Rate limits globais por IP/usuário

#### QoS e Resiliência (Ocelot)
- Circuit Breaker: 3 falhas consecutivas → 30s break
- Timeout: 10s por request
- Retry policies (configurável)

#### CORS (Middlewares ASP.NET Core)
- Configurado no código, não em ingress plugins
- Permite origins configuráveis
- Credentials support

#### Observabilidade
- **Logs**: Serilog (console + arquivos)
- **Metrics**: Prometheus (`/metrics`)
- **Tracing**: OpenTelemetry (OTLP exporter)
- **Correlation ID**: Propagado via `X-Correlation-Id` header

### 3. AWS Application Load Balancer (Ingress)

```yaml
annotations:
  kubernetes.io/ingress.class: alb
  alb.ingress.kubernetes.io/scheme: internet-facing
  alb.ingress.kubernetes.io/target-type: ip
  alb.ingress.kubernetes.io/listen-ports: '[{"HTTP": 80}, {"HTTPS": 443}]'
  alb.ingress.kubernetes.io/ssl-redirect: '443'
  alb.ingress.kubernetes.io/healthcheck-path: /health
```

**Responsabilidades**:
- ✅ TLS/SSL termination (via ACM)
- ✅ Health checks (`/health`)
- ✅ Roteamento para pods via IP direto (target-type: ip)
- ✅ Auto-scaling integration (Target Groups dinâmicos)

### 4. Comunicação Entre Namespaces

**DNS Interno Kubernetes**:
```
<service-name>.<namespace>.svc.cluster.local
```

**Exemplos** (usados em ocelot.json):
- `identity-api-service.agrosolutions-identity` → Identity Service
- `keycloak-service.agrosolutions-identity:8080` → Keycloak

**Network Policies** (se configurado):
- API Gateway pode acessar todos os namespaces downstream
- Downstream services NÃO podem acessar uns aos outros diretamente

---

## 🔐 Fluxo de Autenticação Completo

### 1. Login de Usuário

```
1. Client → API Gateway
   POST https://api.agrosolutions.com/identity/v1/login
   Body: {"username": "user", "password": "pass"}
   
2. API Gateway → Identity Service (sem auth, rota anônima)
   POST http://identity-api-service.agrosolutions-identity/v1/login
   
3. Identity Service → Keycloak
   POST http://keycloak-service.agrosolutions-identity:8080/realms/master/protocol/openid-connect/token
   Body: client_credentials
   
4. Keycloak ← Identity Service
   Response: {"access_token": "eyJhbG...", "refresh_token": "..."}
   
5. API Gateway ← Identity Service
   Response: {"data": {"accessToken": "eyJhbG...", ...}}
   
6. Client ← API Gateway
   Response: {"data": {"accessToken": "eyJhbG...", ...}}
```

### 2. Acesso a Recurso Protegido

```
1. Client → API Gateway
   GET https://api.agrosolutions.com/identity/v1/users
   Header: Authorization: Bearer eyJhbG...
   
2. API Gateway valida JWT:
   - Verifica assinatura (Keycloak public key)
   - Valida issuer, audience, expiration
   - Extrai claims: scope, roles
   
3. API Gateway verifica RouteClaimsRequirement:
   - Route requer: scope="users:read"
   - Token contém: scope="users:read users:manage"
   - ✅ Autorizado
   
4. API Gateway → Identity Service (com JWT propagado)
   GET http://identity-api-service.agrosolutions-identity/v1/users
   Header: Authorization: Bearer eyJhbG...
   
5. Identity Service processa request
   - Valida token novamente (defense in depth)
   - Retorna dados dos usuários
   
6. API Gateway ← Identity Service
   Response: [{"id": 1, "name": "User 1"}, ...]
   
7. Client ← API Gateway
   Response: [{"id": 1, "name": "User 1"}, ...]
```

---

## 📊 Endpoints Públicos (via API Gateway)

| Endpoint Pattern | Downstream Service | Auth Required | Scopes Required |
|-----------------|-------------------|---------------|-----------------|
| `/health`, `/health/ready`, `/health/live` | API Gateway | ❌ | - |
| `/metrics` | API Gateway | ❌ | - |
| `/identity/v1/login` | Identity Service | ❌ | - |
| `/identity/v1/register` | Identity Service | ❌ | - |
| `/identity/v1/users` (GET) | Identity Service | ✅ | `users:read` |
| `/identity/v1/users` (POST/PUT/DELETE) | Identity Service | ✅ | `users:manage` |
| `/gestao/*` (GET) | Gestão Service | ✅ | `users:read` |
| `/gestao/*` (POST/PUT/DELETE) | Gestão Service | ✅ | `users:manage` |
| `/ingestao/*` | Ingestão Service | ✅ | `users:manage` |
| `/telemetria/*` | Telemetria Service | ✅ | `users:read` |

---

## 🚀 Deploy e Escalabilidade

### Horizontal Pod Autoscaling (HPA)

```yaml
minReplicas: 3
maxReplicas: 10
metrics:
  - CPU: 70%
  - Memory: 80%
```

**Comportamento**:
- **Scale Up**: Imediato (100%/30s ou +2 pods/30s)
- **Scale Down**: Gradual (50%/60s após 5min estável)

### Resources

```yaml
requests:
  cpu: 500m
  memory: 512Mi
limits:
  cpu: 2000m
  memory: 2Gi
```

### Alta Disponibilidade

- **Anti-Affinity**: Pods distribuídos em nodes diferentes
- **Rolling Updates**: `maxUnavailable: 0`, `maxSurge: 1`
- **Grace Period**: 30s para shutdown gracioso

---

## 🔧 Configuração de Desenvolvimento

### Acessar API Gateway Localmente

```bash
# Port-forward
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Testar
curl http://localhost:8080/health
curl http://localhost:8080/identity/v1/login -d '{"username":"user","password":"pass"}' -H "Content-Type: application/json"
```

### Testar Conectividade com Downstream Services

```bash
POD=$(kubectl get pods -n agrosolutions-gateway -l app=api-gateway -o jsonpath='{.items[0].metadata.name}')

# Testar DNS
kubectl exec -n agrosolutions-gateway $POD -- nslookup identity-api-service.agrosolutions-identity

# Testar conectividade HTTP
kubectl exec -n agrosolutions-gateway $POD -- wget -qO- http://identity-api-service.agrosolutions-identity/health
kubectl exec -n agrosolutions-gateway $POD -- wget -qO- http://keycloak-service.agrosolutions-identity:8080/health
```

### Verificar Configuração Ocelot

```bash
POD=$(kubectl get pods -n agrosolutions-gateway -l app=api-gateway -o jsonpath='{.items[0].metadata.name}')

kubectl exec -n agrosolutions-gateway $POD -- cat /app/ocelot.json
```

---

## 📈 Monitoramento e Observabilidade

### Métricas Prometheus

```bash
# Port-forward
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Acessar métricas
curl http://localhost:8080/metrics
```

**Métricas Importantes**:
- `http_requests_total` - Total de requests
- `http_request_duration_seconds` - Latência
- `ocelot_request_total` - Requests por rota
- `process_cpu_seconds_total` - CPU usage
- `dotnet_total_memory_bytes` - Memory usage

### Logs Estruturados (Serilog)

```bash
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway
```

**Formato**:
```json
{
  "Timestamp": "2026-02-18T10:30:00Z",
  "Level": "Information",
  "MessageTemplate": "HTTP {Method} {Path} responded {StatusCode} in {Elapsed}ms",
  "Properties": {
    "Method": "GET",
    "Path": "/identity/v1/users",
    "StatusCode": 200,
    "Elapsed": 45,
    "CorrelationId": "abc123"
  }
}
```

### Distributed Tracing (OpenTelemetry)

```yaml
OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector-service:4317"
OTEL_SERVICE_NAME: "agrosolutions-api-gateway"
```

**Trace Context Propagation**:
- `traceparent` header propagado para downstream services
- Correlation ID incluído em traces

---

## 🔒 Segurança

### Network Policies

- API Gateway pode acessar: Identity, Gestão, Ingestão, Telemetria services
- Downstream services NÃO podem acessar uns aos outros diretamente
- Apenas API Gateway tem acesso público (via ALB)

### JWT Validation

```yaml
Jwt__Authority: "http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions"
Jwt__Audience: "agrosolutions-api"
```

- Public key fetched de Keycloak JWKS endpoint
- Token signature validation
- Issuer, audience, expiration validation
- Claims extraction e validation

### Secrets Management

```bash
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='...' \
  -n agrosolutions-gateway
```

Secrets montados como variáveis de ambiente (não em disco).

---

## 📚 Referências

- **Ocelot Configuration**: [configmaps.yaml](../k8s/production/configmaps.yaml)
- **Deployment**: [deployment.yaml](../k8s/production/deployment.yaml)
- **Ingress**: [ingress-aws.yaml](../k8s/production/ingress-aws.yaml)
- **Ocelot Docs**: https://ocelot.readthedocs.io/
- **AWS Load Balancer Controller**: https://kubernetes-sigs.github.io/aws-load-balancer-controller/

---

**Desenvolvido com ❤️ pela equipe AgroSolutions**
