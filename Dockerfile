# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar arquivos do projeto
COPY ["src/AgroSolutions.ApiGateway/AgroSolutions.ApiGateway.csproj", "src/AgroSolutions.ApiGateway/"]
RUN dotnet restore "src/AgroSolutions.ApiGateway/AgroSolutions.ApiGateway.csproj"

# Copiar todo o código fonte
COPY . .
WORKDIR "/src/src/AgroSolutions.ApiGateway"

# Build da aplicação
RUN dotnet build "AgroSolutions.ApiGateway.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "AgroSolutions.ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Criar usuário não-root para segurança
RUN addgroup --system --gid 1000 appuser \
    && adduser --system --uid 1000 --ingroup appuser --shell /bin/sh appuser

# Copiar arquivos publicados
COPY --from=publish /app/publish .

# Criar diretório de logs
RUN mkdir -p /app/logs && chown -R appuser:appuser /app

# Configurar usuário não-root
USER appuser

# Expor portas
EXPOSE 80
EXPOSE 443

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

ENTRYPOINT ["dotnet", "AgroSolutions.ApiGateway.dll"]
