# 📝 Comandos Úteis - AgroSolutions API Gateway

Este documento contém comandos úteis para operação e troubleshooting do API Gateway.

> **⚠️ Arquitetura**: O API Gateway é o **único ponto de entrada público**. Downstream services  
> (Identity, Gestão, etc.) são **ClusterIP** (acesso interno apenas via API Gateway).

---

## 🚀 Deploy e Rollback

### Deploy Manual Completo
```bash
# Aplicar todos os manifestos na ordem
kubectl apply -f k8s/production/namespace.yaml
kubectl apply -f k8s/production/configmaps.yaml
kubectl apply -f k8s/production/services.yaml
kubectl apply -f k8s/production/deployment.yaml
kubectl apply -f k8s/production/hpa.yaml
kubectl apply -f k8s/production/ingress-aws.yaml  # OPCIONAL - ALB para acesso direto
kubectl apply -f k8s/production/observability.yaml
```

### Rollback
```bash
# Ver histórico
kubectl rollout history deployment/api-gateway -n agrosolutions-gateway

# Rollback para versão anterior
kubectl rollout undo deployment/api-gateway -n agrosolutions-gateway

# Rollback para versão específica
kubectl rollout undo deployment/api-gateway -n agrosolutions-gateway --to-revision=3
```

### Restart Deployment
```bash
kubectl rollout restart deployment/api-gateway -n agrosolutions-gateway
```

---

## 🔍 Monitoramento e Logs

### Ver Pods
```bash
# Todos os pods
kubectl get pods -n agrosolutions-gateway

# Pods com informações detalhadas
kubectl get pods -n agrosolutions-gateway -o wide

# Watch (atualização em tempo real)
kubectl get pods -n agrosolutions-gateway -w
```

### Logs
```bash
# Logs do API Gateway
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Logs do último pod que crashou
kubectl logs <pod-name> -n agrosolutions-gateway --previous

# Logs de todos os containers do deployment
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway --all-containers=true

# Logs com timestamp
kubectl logs deployment/api-gateway -n agrosolutions-gateway --timestamps=true

# Tail dos últimos 100 logs
kubectl logs deployment/api-gateway -n agrosolutions-gateway --tail=100
```

### Eventos
```bash
# Ver eventos recentes
kubectl get events -n agrosolutions-gateway --sort-by='.lastTimestamp'

# Watch de eventos
kubectl get events -n agrosolutions-gateway -w
```

### Métricas
```bash
# CPU e Memória dos pods
kubectl top pods -n agrosolutions-gateway

# CPU e Memória dos nodes
kubectl top nodes

# HPA status
kubectl get hpa -n agrosolutions-gateway
kubectl describe hpa api-gateway-hpa -n agrosolutions-gateway
```

---

## 🐛 Troubleshooting

### Verificar Status do Deployment
```bash
kubectl get deployment api-gateway -n agrosolutions-gateway
kubectl describe deployment api-gateway -n agrosolutions-gateway
```

### Verificar Status dos Pods
```bash
kubectl get pods -n agrosolutions-gateway
kubectl describe pod <pod-name> -n agrosolutions-gateway
```

### Executar comandos dentro do pod
```bash
# Shell interativo
kubectl exec -it <pod-name> -n agrosolutions-gateway -- /bin/sh

# Verificar configuração Ocelot
kubectl exec <pod-name> -n agrosolutions-gateway -- cat /app/ocelot.json

# Testar health check
kubectl exec <pod-name> -n agrosolutions-gateway -- wget -qO- http://localhost:80/health

# Testar conectividade com downstream services
kubectl exec <pod-name> -n agrosolutions-gateway -- wget -qO- http://identity-api-service.agrosolutions-identity/health
```

### Verificar ConfigMaps
```bash
kubectl get configmaps -n agrosolutions-gateway
kubectl describe configmap api-gateway-config -n agrosolutions-gateway
kubectl describe configmap ocelot-config -n agrosolutions-gateway
```

### Verificar Services
```bash
kubectl get services -n agrosolutions-gateway
kubectl describe service api-gateway-service -n agrosolutions-gateway
```

### Verificar Ingress
```bash
kubectl get ingress -n agrosolutions-gateway
kubectl describe ingress api-gateway-ingress -n agrosolutions-gateway
```

### Verificar Secrets
```bash
kubectl get secrets -n agrosolutions-gateway
kubectl describe secret jwt-secrets -n agrosolutions-gateway
```

---

## 🔐 Gerenciamento de Secrets

### Criar Secrets
```bash
# JWT Secrets
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions' \
  --namespace=agrosolutions-gateway \
  --dry-run=client -o yaml | kubectl apply -f -
```

### Atualizar Secrets
```bash
# Deletar e recriar
kubectl delete secret jwt-secrets -n agrosolutions-gateway
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions' \
  -n agrosolutions-gateway
```

### Ver conteúdo dos Secrets (base64 encoded)
```bash
kubectl get secret jwt-secrets -n agrosolutions-gateway -o yaml
```

---

## 📊 Observabilidade

### Prometheus Metrics
```bash
# Port-forward para acessar métricas localmente
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Acessar no browser
curl http://localhost:8080/metrics
```

### ServiceMonitor Status
```bash
kubectl get servicemonitor -n agrosolutions-gateway
kubectl describe servicemonitor api-gateway-metrics -n agrosolutions-gateway
```

---

## 🧹 Limpeza

### Deletar recursos (cuidado!)
```bash
# Deletar deployment
kubectl delete deployment api-gateway -n agrosolutions-gateway

# Deletar todos os recursos do namespace
kubectl delete all --all -n agrosolutions-gateway

# Deletar namespace (remove tudo)
kubectl delete namespace agrosolutions-gateway
```

---

## 🔄 Atualização de Configuração

### Atualizar ConfigMap e reiniciar pods
```bash
# Editar ConfigMap
kubectl edit configmap api-gateway-config -n agrosolutions-gateway

# Reiniciar deployment para aplicar mudanças
kubectl rollout restart deployment/api-gateway -n agrosolutions-gateway
```

### Atualizar Ocelot.json
```bash
# Editar ocelot.json via ConfigMap
kubectl edit configmap ocelot-config -n agrosolutions-gateway

# Reiniciar deployment
kubectl rollout restart deployment/api-gateway -n agrosolutions-gateway
```

---

## 📈 Escalonamento

### Escalar manualmente
```bash
# Escalar para 5 replicas
kubectl scale deployment api-gateway --replicas=5 -n agrosolutions-gateway

# Verificar status
kubectl get deployment api-gateway -n agrosolutions-gateway
```

### HPA Auto-scaling
```bash
# Ver status do HPA
kubectl get hpa -n agrosolutions-gateway

# Detalhes do HPA
kubectl describe hpa api-gateway-hpa -n agrosolutions-gateway

# Editar limites do HPA
kubectl edit hpa api-gateway-hpa -n agrosolutions-gateway
```

---

## 🌐 Testes de Conectividade

### Testar rota do Identity Service
```bash
# Port-forward do API Gateway
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Testar health
curl http://localhost:8080/health

# Testar rota do identity (necessita token)
curl http://localhost:8080/identity/v1/... -H "Authorization: Bearer <TOKEN>"
```

### Testar conectividade entre namespaces
```bash
# Do API Gateway para Identity Service
kubectl exec -it <gateway-pod> -n agrosolutions-gateway -- \
  wget -qO- http://identity-api-service.agrosolutions-identity/health

# Do API Gateway para Keycloak
kubectl exec -it <gateway-pod> -n agrosolutions-gateway -- \
  wget -qO- http://keycloak-service.agrosolutions-identity:8080/health
```

---

## 📦 Build e Push de Imagem

### Build local e push para ECR
```bash
# Login no ECR
aws ecr get-login-password --region sa-east-1 | \
  docker login --username AWS --password-stdin 316295889438.dkr.ecr.sa-east-1.amazonaws.com

# Build da imagem
docker build -t agrosolutions-api-gateway:latest .

# Tag da imagem
docker tag agrosolutions-api-gateway:latest \
  316295889438.dkr.ecr.sa-east-1.amazonaws.com/agrosolutions-api-gateway:latest

# Push para ECR
docker push 316295889438.dkr.ecr.sa-east-1.amazonaws.com/agrosolutions-api-gateway:latest
```

### Forçar pull de nova imagem
```bash
# Atualizar deployment para forçar pull
kubectl set image deployment/api-gateway \
  api-gateway=316295889438.dkr.ecr.sa-east-1.amazonaws.com/agrosolutions-api-gateway:latest \
  -n agrosolutions-gateway

# Verificar rollout
kubectl rollout status deployment/api-gateway -n agrosolutions-gateway
```

---

## 🔍 Debugging Avançado

### Verificar DNS interno
```bash
kubectl exec -it <pod-name> -n agrosolutions-gateway -- nslookup identity-api-service.agrosolutions-identity
kubectl exec -it <pod-name> -n agrosolutions-gateway -- nslookup keycloak-service.agrosolutions-identity
```

### Verificar variáveis de ambiente
```bash
kubectl exec <pod-name> -n agrosolutions-gateway -- env | sort
```

### Analisar recursos em tempo real
```bash
# CPU e memória em tempo real
kubectl top pod <pod-name> -n agrosolutions-gateway

# Describe completo
kubectl describe pod <pod-name> -n agrosolutions-gateway
```

---

## 📚 Referências Rápidas

### Namespaces relacionados
- `agrosolutions-gateway` - API Gateway (Ocelot)
- `agrosolutions-identity` - Identity Service e Keycloak
- `kube-system` - AWS Load Balancer Controller

### Services importantes
- `api-gateway-service` - API Gateway (porta 80)
- `identity-api-service.agrosolutions-identity` - Identity API (porta 80)
- `keycloak-service.agrosolutions-identity` - Keycloak (porta 8080)

### Health Endpoints
- `/health` - Health check geral
- `/health/ready` - Readiness probe
- `/health/live` - Liveness probe
- `/metrics` - Prometheus metrics
