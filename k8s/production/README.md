# 📦 AgroSolutions API Gateway - Kubernetes Deployment Guide

## 📋 Índice
- [Pré-requisitos](#%EF%B8%8F-pré-requisitos)
- [Configuração Inicial](#-configuração-inicial)
- [Deploy Manual](#-deploy-manual-kubectl)
- [CI/CD com GitHub Actions](#-cicd-com-github-actions)
- [Verificação e Troubleshooting](#-verificação-e-troubleshooting)
- [Acesso aos Serviços](#-acesso-aos-serviços)

---

## ⚙️ Pré-requisitos

### Ferramentas Necessárias
- [kubectl](https://kubernetes.io/docs/tasks/tools/) v1.28+
- [AWS CLI](https://aws.amazon.com/cli/) v2.x
- [Helm](https://helm.sh/docs/intro/install/) v3.12+ (para AWS Load Balancer Controller)
- Docker (para build local)

### Recursos AWS
- **AWS Account ID**: `316295889438`
- **Region**: `sa-east-1` (São Paulo)
- **EKS Cluster**: `agrosolutions-eks-cluster`
- **ECR Repository**: `agrosolutions-api-gateway`
- **IAM Role**: Configurado com permissões EKS e ECR

### Serviços Prerequisitos
Certifique-se de que os seguintes serviços estão deployados:
- ✅ Identity Service (`agrosolutions-identity` namespace)
- ✅ Keycloak (`agrosolutions-identity` namespace)
- ✅ AWS Load Balancer Controller (cluster-wide)

> **Nota**: O API Gateway depende do Identity Service e Keycloak para autenticação JWT.

---

## 🔧 Configuração Inicial

### 1. Configurar AWS CLI

```bash
aws configure
# AWS Access Key ID: [sua chave]
# AWS Secret Access Key: [seu secret]
# Default region name: sa-east-1
# Default output format: json
```

### 2. Conectar ao Cluster EKS

```bash
aws eks update-kubeconfig --name agrosolutions-eks-cluster --region sa-east-1
kubectl cluster-info
kubectl get nodes
```

### 3. Instalar AWS Load Balancer Controller (se ainda não estiver instalado)

```bash
# Criar IAM Policy para AWS Load Balancer Controller
curl -o iam-policy.json https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/v2.7.0/docs/install/iam_policy.json

aws iam create-policy \
  --policy-name AWSLoadBalancerControllerIAMPolicy \
  --policy-document file://iam-policy.json

# Criar IAM Role para Service Account
eksctl create iamserviceaccount \
  --cluster=agrosolutions-eks-cluster \
  --namespace=kube-system \
  --name=aws-load-balancer-controller \
  --attach-policy-arn=arn:aws:iam::316295889438:policy/AWSLoadBalancerControllerIAMPolicy \
  --override-existing-serviceaccounts \
  --approve

# Instalar AWS Load Balancer Controller via Helm
helm repo add eks https://aws.github.io/eks-charts
helm repo update

helm install aws-load-balancer-controller eks/aws-load-balancer-controller \
  -n kube-system \
  --set clusterName=agrosolutions-eks-cluster \
  --set serviceAccount.create=false \
  --set serviceAccount.name=aws-load-balancer-controller

# Verificar instalação
kubectl get deployment -n kube-system aws-load-balancer-controller
kubectl get pods -n kube-system -l app.kubernetes.io/name=aws-load-balancer-controller
```

### 4. Arquitetura e Fluxo de Tráfego

**Fluxo de Requisições**:
```
Internet → AWS ALB → API Gateway (Ocelot) → Downstream Services
                          |
                          ├─→ Identity Service (/identity/*)
                          ├─→ Gestão Service (/gestao/*)
                          ├─→ Ingestão Service (/ingestao/*)
                          └─→ Telemetria Service (/telemetria/*)
```

**Características**:
- ✅ API Gateway é o **único ponto de entrada público**
- ✅ Ocelot gerencia roteamento, rate limiting, JWT auth
- ✅ Downstream services são **ClusterIP** (acesso interno apenas)
- ✅ AWS ALB gerencia TLS/SSL termination e health checks

### 5. Criar Secrets do Kubernetes

**⚠️ IMPORTANTE**: Nunca commite secrets no Git! Use o comando abaixo para criar os secrets necessários:

```bash
# JWT Secrets (opcional - usado se necessário)
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions' \
  -n agrosolutions-gateway
```

### 6. Verificar Conectividade com Identity Service

```bash
# Verificar se o Identity Service está rodando
kubectl get pods -n agrosolutions-identity

# Verificar se o service está acessível
kubectl get svc identity-api-service -n agrosolutions-identity
kubectl get svc keycloak-service -n agrosolutions-identity
```

---

## 🚀 Deploy Manual (kubectl)

### Ordem de Deploy

```bash
# 1. Criar namespace
kubectl apply -f k8s/production/namespace.yaml

# 2. Criar ConfigMaps (inclui configuração Ocelot)
kubectl apply -f k8s/production/configmaps.yaml

# 3. Criar Services
kubectl apply -f k8s/production/services.yaml

# 4. Deploy do API Gateway
kubectl apply -f k8s/production/deployment.yaml

# Aguardar pods ficarem prontos
kubectl wait --for=condition=ready pod \
  -l app=api-gateway \
  -n agrosolutions-gateway \
  --timeout=300s

# 5. Configurar HPA (Auto-scaling)
kubectl apply -f k8s/production/hpa.yaml

# 6. Configurar Ingress (AWS ALB - OPCIONAL para acesso direto)
kubectl apply -f k8s/production/ingress-aws.yaml

# 7. Configurar Observabilidade (Prometheus)
kubectl apply -f k8s/production/observability.yaml
```

### Verificar Deploy

```bash
# Ver status dos pods
kubectl get pods -n agrosolutions-gateway

# Ver logs
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Ver status do deployment
kubectl get deployment api-gateway -n agrosolutions-gateway

# Ver HPA
kubectl get hpa -n agrosolutions-gateway

# Ver ingress
kubectl get ingress -n agrosolutions-gateway
```

---

## 🤖 CI/CD com GitHub Actions

### Configuração de Secrets no GitHub

Acesse: `Settings > Secrets and variables > Actions` e adicione:

| Secret Name | Description | Example |
|------------|-------------|---------|
| `AWS_ROLE_TO_ASSUME` | ARN da IAM Role para OIDC | `arn:aws:iam::316295889438:role/GitHubActionsRole` |
| `JWT_ISSUER` | Issuer JWT do Keycloak | `http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions` |

### Workflows Disponíveis

#### 1. **Build and Push to ECR** (`.github/workflows/build.yml`)

**Trigger**: Push em `main` ou `develop` que altere `src/`, `Dockerfile` ou workflows

**Passos**:
1. ✅ Run tests (dotnet test)
2. 🏗️ Build Docker image
3. 🔒 Trivy security scan
4. 📤 Push to ECR

**Uso Manual**:
```bash
# Push para branch main
git add .
git commit -m "feat: nova feature"
git push origin main

# GitHub Actions irá detectar e executar automaticamente
```

#### 2. **Deploy to EKS** (`.github/workflows/deploy.yml`)

**Trigger**:
- Push em `main` que altere `k8s/production/**`
- Workflow dispatch manual (pode escolher environment)

**Passos**:
1. 🔐 Configure AWS credentials
2. ☸️ Update kubeconfig
3. 📦 Apply Kubernetes manifests
4. ✅ Verify deployment
5. 🏥 Run health check

**Uso Manual via GitHub UI**:
1. Acesse: `Actions > Deploy to EKS > Run workflow`
2. Selecione branch: `main`
3. Escolha environment: `production` ou `staging`
4. Clique em `Run workflow`

**Uso Manual via CLI**:
```bash
# Usando GitHub CLI (gh)
gh workflow run deploy.yml \
  -f environment=production
```

### Monitorar Workflows

```bash
# Listar workflows
gh workflow list

# Ver runs do workflow de build
gh run list --workflow=build.yml

# Ver logs do último run
gh run view --log
```

---

## 🔍 Verificação e Troubleshooting

### Health Checks

```bash
# Port-forward para testar localmente
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Testar endpoints (em outro terminal)
curl http://localhost:8080/health
curl http://localhost:8080/health/ready
curl http://localhost:8080/health/live
curl http://localhost:8080/metrics
```

### Verificar Logs

```bash
# Logs em tempo real
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Últimos 100 logs
kubectl logs deployment/api-gateway -n agrosolutions-gateway --tail=100

# Logs com timestamp
kubectl logs deployment/api-gateway -n agrosolutions-gateway --timestamps=true
```

### Verificar Conectividade com Downstream Services

```bash
# Pegar nome do pod
POD_NAME=$(kubectl get pods -n agrosolutions-gateway -l app=api-gateway -o jsonpath='{.items[0].metadata.name}')

# Testar conectividade com Identity Service
kubectl exec -n agrosolutions-gateway $POD_NAME -- \
  wget -qO- http://identity-api-service.agrosolutions-identity/health

# Testar conectividade com Keycloak
kubectl exec -n agrosolutions-gateway $POD_NAME -- \
  wget -qO- http://keycloak-service.agrosolutions-identity:8080/health
```

### Verificar Configuração Ocelot

```bash
# Ver configuração Ocelot carregada
POD_NAME=$(kubectl get pods -n agrosolutions-gateway -l app=api-gateway -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n agrosolutions-gateway $POD_NAME -- cat /app/ocelot.json
```

### Problemas Comuns

#### ❌ Pods não iniciam (CrashLoopBackOff)

```bash
# Ver detalhes do pod
kubectl describe pod <pod-name> -n agrosolutions-gateway

# Ver logs do container que crashou
kubectl logs <pod-name> -n agrosolutions-gateway --previous
```

**Soluções**:
- Verificar se ConfigMap `ocelot-config` foi aplicado
- Verificar se variáveis de ambiente estão corretas
- Verificar conectividade com Identity Service

#### ❌ Erro de autenticação JWT

```bash
# Verificar configuração JWT
kubectl get configmap api-gateway-config -n agrosolutions-gateway -o yaml | grep Jwt
```

**Soluções**:
- Confirmar que `Jwt__Authority` aponta para Keycloak correto
- Verificar se Keycloak está acessível

#### ❌ Rotas retornam 502 Bad Gateway

```bash
# Verificar se downstream services estão rodando
kubectl get pods -n agrosolutions-identity
kubectl get svc -n agrosolutions-identity
```

**Soluções**:
- Verificar se Identity Service está rodando
- Verificar DNS interno: `identity-api-service.agrosolutions-identity`
- Verificar logs do API Gateway

---

## 🌐 Acesso aos Serviços

### Acesso via Ingress (Produção)

```bash
# Obter DNS do AWS Application Load Balancer
kubectl get ingress api-gateway-ingress -n agrosolutions-gateway

# Anotar o ADDRESS/HOSTNAME do ALB
```

**Configurar DNS**:
Configure seu DNS para apontar para o ALB:
```
api.agrosolutions.com → <ALB_DNS_NAME>
```

**Endpoints Públicos**:
- `https://api.agrosolutions.com/health` - Health check
- `https://api.agrosolutions.com/identity/v1/...` - Identity Service routes
- `https://api.agrosolutions.com/gestao/...` - Gestão Service routes
- `https://api.agrosolutions.com/metrics` - Prometheus metrics

### Acesso Local (Port-Forward)

```bash
# Port-forward do API Gateway
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Acessar localmente
curl http://localhost:8080/health
```

---

## 📊 Observabilidade

### Prometheus Metrics

```bash
# Port-forward para acessar métricas
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Scrape metrics
curl http://localhost:8080/metrics
```

### ServiceMonitor (Prometheus Operator)

Se você tem Prometheus Operator instalado:

```bash
# Verificar ServiceMonitor
kubectl get servicemonitor -n agrosolutions-gateway

# Ver detalhes
kubectl describe servicemonitor api-gateway-metrics -n agrosolutions-gateway
```

### Grafana Dashboards

Importe dashboards recomendados:
- **Ocelot Gateway**: Dashboard customizado para métricas do Ocelot
- **ASP.NET Core**: Dashboard ID `10915`
- **Kubernetes Pods**: Dashboard ID `6417`

---

## 🔄 Atualização e Rollback

### Atualizar Deployment

```bash
# Atualizar imagem
kubectl set image deployment/api-gateway \
  api-gateway=316295889438.dkr.ecr.sa-east-1.amazonaws.com/agrosolutions-api-gateway:v2.0.0 \
  -n agrosolutions-gateway

# Acompanhar rollout
kubectl rollout status deployment/api-gateway -n agrosolutions-gateway
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

### Atualizar ConfigMap

```bash
# Editar ConfigMap
kubectl edit configmap api-gateway-config -n agrosolutions-gateway

# Ou editar Ocelot config
kubectl edit configmap ocelot-config -n agrosolutions-gateway

# Reiniciar deployment para recarregar configuração
kubectl rollout restart deployment/api-gateway -n agrosolutions-gateway
```

---

## 📈 Escalonamento

### Manual Scaling

```bash
# Escalar para 5 replicas
kubectl scale deployment api-gateway --replicas=5 -n agrosolutions-gateway
```

### Auto-scaling (HPA)

```bash
# Ver status do HPA
kubectl get hpa api-gateway-hpa -n agrosolutions-gateway

# Detalhes
kubectl describe hpa api-gateway-hpa -n agrosolutions-gateway
```

**Configuração Atual**:
- **Min replicas**: 3
- **Max replicas**: 10
- **Target CPU**: 70%
- **Target Memory**: 80%

---

## 🧹 Limpeza

### Remover Deployment

```bash
kubectl delete deployment api-gateway -n agrosolutions-gateway
```

### Remover Namespace Completo

```bash
# ⚠️ CUIDADO: Remove TODOS os recursos do API Gateway
kubectl delete namespace agrosolutions-gateway
```

---

## 📚 Arquivos de Configuração

| Arquivo | Descrição |
|---------|-----------|
| `namespace.yaml` | Define namespace `agrosolutions-gateway` |
| `configmaps.yaml` | Configurações da aplicação + Ocelot.json |
| `deployment.yaml` | Deployment do API Gateway (3 replicas) |
| `services.yaml` | Service ClusterIP na porta 80 |
| `ingress-aws.yaml` | AWS ALB Ingress com health checks |
| `hpa.yaml` | Auto-scaling (3-10 replicas) |
| `observability.yaml` | ServiceMonitor para Prometheus |

---

## 🔗 Links Úteis

- [Ocelot Documentation](https://ocelot.readthedocs.io/)
- [AWS Load Balancer Controller](https://kubernetes-sigs.github.io/aws-load-balancer-controller/)
- [Prometheus Operator](https://prometheus-operator.dev/)
- [AWS EKS Best Practices](https://aws.github.io/aws-eks-best-practices/)
- [Comandos Úteis](./COMMANDS.md)

---

## 🆘 Suporte

Para troubleshooting avançado, consulte:
- [COMMANDS.md](./COMMANDS.md) - Lista completa de comandos
- Logs do Identity Service (dependência)
- Logs do Keycloak (autenticação)
- Métricas no Prometheus/Grafana

---

**Desenvolvido com ❤️ pela equipe AgroSolutions**
