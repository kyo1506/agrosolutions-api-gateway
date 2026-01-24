# AgroSolutions - API Gateway

API Gateway desenvolvido com Ocelot para orquestração de microsserviços da plataforma AgroSolutions (Agricultura 4.0).

## 🌟 Visão Geral

O **AgroSolutions API Gateway** é o ponto de entrada unificado para todos os microsserviços da plataforma de Agricultura de Precisão. Implementado com Ocelot em .NET 10, segue princípios de Clean Architecture, SOLID e Clean Code.

### Principais Funcionalidades

- ✅ **Roteamento Inteligente**: Direcionamento de requisições para microsserviços específicos
- ✅ **Rate Limiting**: Proteção contra sobrecarga com políticas personalizadas
- ✅ **Circuit Breaker**: Resiliência com padrão de Circuit Breaker (QoS)
- ✅ **Load Balancing**: Distribuição de carga entre instâncias
- ✅ **Caching**: Cache distribuído para otimização de performance
- ✅ **Autenticação JWT**: Validação centralizada de tokens
- ✅ **Correlation ID**: Rastreamento distribuído de requisições
- ✅ **Observabilidade**: Métricas Prometheus, logs estruturados (Serilog), tracing
- ✅ **Health Checks**: Monitoramento da saúde dos serviços

## 🏗️ Arquitetura

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ▼
┌─────────────────────────────────────┐
│      API Gateway (Ocelot)           │
│  ┌───────────────────────────────┐  │
│  │  Middlewares Customizados     │  │
│  │  - CorrelationId              │  │
│  │  - Request Logging            │  │
│  │  - Exception Handling         │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │  Rate Limiting & Auth         │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │  Ocelot Routing Engine        │  │
│  └───────────────────────────────┘  │
└─────────────┬───────────────────────┘
              │
    ┌─────────┴─────────┬─────────────┬──────────────┐
    ▼                   ▼             ▼              ▼
┌─────────┐      ┌──────────┐  ┌──────────┐  ┌──────────┐
│ Gestão  │      │ Ingestão │  │Telemetria│  │ Alertas  │
│   API   │      │   API    │  │   API    │  │   API    │
└─────────┘      └──────────┘  └──────────┘  └──────────┘
```

## 🚀 Tecnologias

- **.NET 10**: Framework principal
- **Ocelot 23.3.4**: Engine do API Gateway
- **Serilog**: Logging estruturado
- **Prometheus**: Métricas e observabilidade
- **OpenTelemetry**: Tracing distribuído
- **JWT Bearer**: Autenticação
- **Docker**: Containerização
- **Kubernetes**: Orquestração de containers

## 📦 Estrutura do Projeto

```
agrosolutions-api-gateway/
├── src/
│   └── AgroSolutions.ApiGateway/
│       ├── Configuration/
│       │   ├── JwtAuthenticationExtensions.cs
│       │   └── RateLimitingExtensions.cs
│       ├── Controllers/
│       │   └── InfoController.cs
│       ├── HealthChecks/
│       │   └── DownstreamServicesHealthCheck.cs
│       ├── Middlewares/
│       │   ├── CorrelationIdMiddleware.cs
│       │   ├── ExceptionHandlingMiddleware.cs
│       │   └── RequestLoggingMiddleware.cs
│       ├── ocelot.json
│       ├── ocelot.Development.json
│       ├── appsettings.json
│       └── Program.cs
├── k8s/
│   ├── namespace.yaml
│   ├── deployment.yaml
│   └── ingress.yaml
├── Dockerfile
├── docker-compose.yml
└── README.md
```

## 🔧 Configuração

### Rotas Configuradas

| Rota | Serviço | Métodos | Rate Limit |
|------|---------|---------|------------|
| `/gestao/*` | API de Gestão | GET, POST, PUT, DELETE | 100/min |
| `/ingestao/*` | API de Ingestão | POST | 1000/min |
| `/telemetria/*` | API de Telemetria | GET | 200/min |
| `/alertas/*` | API de Alertas | GET, POST, PUT | 150/min |
| `/dashboard/*` | API de Dashboard | GET | 100/min |

### Variáveis de Ambiente

```bash
# Ambiente
ASPNETCORE_ENVIRONMENT=Production

# URLs
ASPNETCORE_URLS=http://+:80

# JWT (Configure no appsettings.json ou variáveis de ambiente)
Jwt__SecretKey=YourSecretKey
Jwt__Issuer=AgroSolutions
Jwt__Audience=AgroSolutions.Services
```

## 🐳 Docker

### Build da Imagem

```bash
docker build -t agrosolutions/api-gateway:latest .
```

### Executar com Docker Compose

```bash
docker-compose up -d
```

O gateway estará disponível em `http://localhost:5000`

## ☸️ Kubernetes

### Deploy no Kubernetes

```bash
# Criar namespace
kubectl apply -f k8s/namespace.yaml

# Deploy da aplicação
kubectl apply -f k8s/deployment.yaml

# Configurar Ingress
kubectl apply -f k8s/ingress.yaml
```

### Verificar Status

```bash
# Verificar pods
kubectl get pods -n agrosolutions

# Verificar serviços
kubectl get svc -n agrosolutions

# Logs
kubectl logs -f deployment/agrosolutions-api-gateway -n agrosolutions
```

## 📊 Observabilidade

### Endpoints de Monitoramento

- **Health Check**: `GET /health`
- **Readiness**: `GET /health/ready`
- **Liveness**: `GET /health/live`
- **Métricas Prometheus**: `GET /metrics`
- **Informações**: `GET /api/info`
- **Rotas**: `GET /api/info/routes`

### Métricas Disponíveis

- Requisições por rota
- Latência (P50, P95, P99)
- Taxa de erro
- Rate limiting (requests rejeitados)
- Circuit breaker (aberto/fechado)
- Throughput

## 🔒 Segurança

### Implementações de Segurança

1. **Autenticação JWT**: Validação centralizada de tokens
2. **Rate Limiting**: Proteção contra abuso e DDoS
3. **CORS**: Configuração de origens permitidas
4. **Container Security**: Usuário não-root no Docker
5. **Secrets Management**: Uso de Kubernetes Secrets

### Configurar JWT

```json
{
  "Jwt": {
    "Issuer": "AgroSolutions",
    "Audience": "AgroSolutions.Services",
    "SecretKey": "YourSuperSecretKeyHere_AtLeast32Characters!"
  }
}
```

## 🧪 Testes

```bash
# Restaurar dependências
dotnet restore

# Compilar
dotnet build

# Executar testes
dotnet test
```

## 🚦 Rate Limiting

O gateway implementa três políticas de rate limiting:

### 1. Política Padrão (Fixed Window)
- **Limite**: 100 requisições/minuto por IP
- **Aplicação**: Todas as rotas não especificadas

### 2. Política de Ingestão (Sliding Window)
- **Limite**: 1000 requisições/minuto por IP
- **Aplicação**: Rotas de ingestão de dados
- **Vantagem**: Maior throughput sem spikes

### 3. Política de Leitura (Token Bucket)
- **Limite**: 500 tokens, 100 tokens/minuto por usuário
- **Aplicação**: APIs de consulta
- **Vantagem**: Flexibilidade para bursts controlados

## 📝 Logs

Os logs são estruturados e incluem:

- **Correlation ID**: Rastreamento de requisições
- **Timestamp**: Data e hora UTC
- **Level**: Information, Warning, Error
- **Source**: Componente que gerou o log
- **Message**: Mensagem descritiva
- **Properties**: Dados adicionais estruturados

Exemplo de log:
```json
{
  "Timestamp": "2026-01-24T10:30:00.000Z",
  "Level": "Information",
  "MessageTemplate": "Incoming Request: {Method} {Path}",
  "Properties": {
    "Method": "GET",
    "Path": "/gestao/produtores",
    "CorrelationId": "abc123-def456",
    "SourceContext": "AgroSolutions.ApiGateway.Middlewares.RequestLoggingMiddleware"
  }
}
```

## 🎯 Princípios Aplicados

### SOLID

- **Single Responsibility**: Cada middleware tem uma responsabilidade única
- **Open/Closed**: Extensível via configuração e novos middlewares
- **Liskov Substitution**: Interfaces bem definidas
- **Interface Segregation**: Interfaces específicas e coesas
- **Dependency Inversion**: Inversão de controle via DI

### Clean Code

- Nomes descritivos e significativos
- Funções pequenas e focadas
- Comentários apenas quando necessário
- Tratamento de erros consistente
- Código auto-documentado

### Clean Architecture

- Separação de responsabilidades por camadas
- Inversão de dependências
- Independência de frameworks externos
- Testabilidade

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
