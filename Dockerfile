ARG DOTNET_VERSION=10.0

# Build stage  
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build

# Definir argumentos de build para metadata
ARG BUILD_DATE
ARG VERSION
ARG REVISION

WORKDIR /src

# Copiar todos os arquivos do projeto
COPY . .

# Restore e Build
RUN dotnet restore src/AgroSolutions.ApiGateway/AgroSolutions.ApiGateway.csproj
RUN dotnet build src/AgroSolutions.ApiGateway/AgroSolutions.ApiGateway.csproj -c Release --no-restore
RUN dotnet publish src/AgroSolutions.ApiGateway/AgroSolutions.ApiGateway.csproj -c Release -o /app/publish --no-build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS final

# Adicionar labels para metadata
LABEL maintainer="AgroSolutions Team" \
      org.opencontainers.image.title="AgroSolutions API Gateway" \
      org.opencontainers.image.description="Ocelot-based API Gateway for AgroSolutions platform" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${REVISION}" \
      ocelot.version="24.1.0"

# Instalar dependências de segurança e runtime
RUN apk add --no-cache \
    icu-libs \
    ca-certificates \
    tzdata \
    curl \
    && update-ca-certificates

# Criar usuário não-root
RUN addgroup -g 1001 -S appgroup && \
    adduser -u 1001 -S appuser -G appgroup

WORKDIR /app

# Copiar arquivos publicados
COPY --from=build --chown=appuser:appgroup /app/publish .

# Criar diretório de logs
RUN mkdir -p /app/logs && chown -R appuser:appgroup /app/logs

# Configurar variáveis de ambiente
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://+:80 \
    TZ=America/Sao_Paulo

# Trocar para usuário não-root
USER appuser

# Expor porta não-privilegiada
EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:80/health || exit 1

# Usar exec form para sinais corretos
ENTRYPOINT ["dotnet", "AgroSolutions.ApiGateway.dll"]
