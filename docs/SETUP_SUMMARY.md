# 🎯 Resumo: CI/CD e Kubernetes Setup - API Gateway

Este documento resume tudo que foi criado para o deploy do API Gateway, seguindo o padrão do Identity Service.

---

## 📁 Estrutura de Arquivos Criados

### 1. Kubernetes Manifests (`k8s/production/`)

```
k8s/production/
├── namespace.yaml           # Namespace: agrosolutions-gateway
├── configmaps.yaml          # Config da aplicação + Ocelot.json completo
├── deployment.yaml          # Deployment com 3 replicas e anti-affinity
├── services.yaml            # Service ClusterIP na porta 80
├── ingress-aws.yaml         # AWS Application Load Balancer Ingress
├── hpa.yaml                 # Auto-scaling: 3-10 replicas (CPU 70%, Mem 80%)
├── observability.yaml       # ServiceMonitor + PodMonitor para Prometheus
├── README.md                # Guia completo de deployment
└── COMMANDS.md              # Comandos úteis para operação
```

### 2. CI/CD Workflows (`.github/workflows/`)

```
.github/workflows/
├── build.yml                # Build, test, security scan e push para ECR
└── deploy.yml               # Deploy automatizado para EKS
```

### 3. Documentação (`docs/`)

```
docs/
├── DEPLOYMENT_GUIDE.md      # Guia completo de setup AWS e deploy
└── GITHUB_ACTIONS_SETUP.md  # Configuração detalhada do CI/CD
```

### 4. Arquivos Docker

```
.dockerignore                # Otimiza build ignorando arquivos desnecessários
Dockerfile                   # Multi-stage build com Alpine (melhorado)
```

---

## 🚀 Quick Start

### Opção 1: Deploy Manual (Recommended para primeira vez)

```bash
# 1. Conectar ao cluster
aws eks update-kubeconfig --name agrosolutions-eks-cluster --region sa-east-1

# 2. Deploy completo
kubectl apply -f k8s/production/namespace.yaml
kubectl apply -f k8s/production/configmaps.yaml
kubectl apply -f k8s/production/services.yaml
kubectl apply -f k8s/production/deployment.yaml
kubectl apply -f k8s/production/hpa.yaml
kubectl apply -f k8s/production/ingress-aws.yaml
kubectl apply -f k8s/production/observability.yaml

# 3. Verificar
kubectl get pods -n agrosolutions-gateway
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway
```

### Opção 2: Deploy via GitHub Actions

```bash
# 1. Configurar secrets no GitHub (ver seção abaixo)

# 2. Push para main
git add .
git commit -m "feat: configurar CI/CD"
git push origin main

# 3. Workflows executarão automaticamente
# - build.yml: testa e faz push para ECR
# - deploy.yml: aplica manifestos no cluster (se arquivos k8s/ mudaram)
```

---

## ⚙️ Configuração Necessária

### 1. AWS Setup

#### Criar ECR Repository
```bash
aws ecr create-repository \
  --repository-name agrosolutions-api-gateway \
  --region sa-east-1 \
  --image-scanning-configuration scanOnPush=true
```

#### Criar IAM Role para GitHub Actions (OIDC)

**Importante**: Substitua `YOUR_GITHUB_ORG` pela sua organização/usuário.

```bash
export GITHUB_ORG="YOUR_GITHUB_ORG"
export AWS_ACCOUNT_ID="316295889438"  # Ou seu account ID

# 1. Criar OIDC provider (uma vez por conta)
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1

# 2. Criar trust policy
cat > github-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::${AWS_ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:${GITHUB_ORG}/agrosolutions-api-gateway:*"
        }
      }
    }
  ]
}
EOF

# 3. Criar role
aws iam create-role \
  --role-name GitHubActionsAPIGateway \
  --assume-role-policy-document file://github-trust-policy.json

# 4. Anotar ARN (usará nos GitHub Secrets)
aws iam get-role --role-name GitHubActionsAPIGateway --query 'Role.Arn' --output text

# 5. Anexar policies
aws iam attach-role-policy \
  --role-name GitHubActionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEC2ContainerRegistryPowerUser

aws iam attach-role-policy \
  --role-name GitHubActionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEKSClusterPolicy
```

### 2. Kubernetes Setup

#### Configurar RBAC (aws-auth)

```bash
kubectl edit configmap aws-auth -n kube-system
```

Adicionar ao `mapRoles`:
```yaml
- rolearn: arn:aws:iam::316295889438:role/GitHubActionsAPIGateway
  username: github-actions
  groups:
    - system:masters
```

#### Criar Secrets

```bash
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions' \
  -n agrosolutions-gateway
```

### 3. GitHub Secrets

**Settings > Secrets and variables > Actions**

Criar:
- `AWS_ROLE_TO_ASSUME` = `arn:aws:iam::316295889438:role/GitHubActionsAPIGateway`
- `JWT_ISSUER` = `http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions`

---

## 🔄 Workflows Explicados

### Build Workflow (`build.yml`)

**Triggers**:
- Push em `main` ou `develop` que altere `src/`, `Dockerfile` ou workflows
- Pull requests para `main` ou `develop`

**Steps**:
1. ✅ Run tests (`dotnet test`)
2. 🏗️ Build Docker image com metadata (BUILD_DATE, VERSION, REVISION)
3. 🔒 Security scan com Trivy
4. 📤 Push para ECR com tags: `latest`, `{branch}-{sha}`
5. 📊 Upload security results para GitHub Security

**Tags geradas**:
- `latest` - Última versão do main
- `main-a1b2c3d4` - Branch + 8 chars do commit SHA

### Deploy Workflow (`deploy.yml`)

**Triggers**:
- Push em `main` que altere `k8s/production/**`
- Manual dispatch (escolhe environment: production/staging)

**Steps**:
1. 🔐 Configure AWS credentials via OIDC
2. ☸️ Update kubeconfig para EKS
3. 📦 Apply Kubernetes manifests (ordem: namespace → configmaps → services → deployment → hpa → ingress → observability)
4. ⏳ Wait for pods ready (timeout 5min)
5. 🏥 Health check
6. 📊 Deployment summary no GitHub

---

## 📊 Configurações Importantes

### Deployment

- **Replicas**: 3 (min) a 10 (max via HPA)
- **Resources**:
  - Requests: 500m CPU, 512Mi Memory
  - Limits: 2000m CPU, 2Gi Memory
- **Image**: `316295889438.dkr.ecr.sa-east-1.amazonaws.com/agrosolutions-api-gateway:latest`
- **Health Checks**:
  - Readiness: `/health/ready` (delay 30s, period 10s)
  - Liveness: `/health/live` (delay 60s, period 30s)
- **Anti-Affinity**: Pods distribuídos em nodes diferentes

### HPA (Auto-scaling)

- **Min replicas**: 3
- **Max replicas**: 10
- **Targets**:
  - CPU: 70%
  - Memory: 80%
- **Behavior**:
  - Scale up: imediato (max 100% ou 2 pods/30s)
  - Scale down: gradual (50%/60s após 5min estável)

### Ingress (AWS ALB)

- **Host**: `api.agrosolutions.com`
- **Controller**: AWS Load Balancer Controller
- **Features**:
  - Auto TLS/SSL termination (via ACM)
  - Health checks configurados
  - Target type: IP (direto para pods)
  - CORS, Rate Limiting gerenciados pelo Ocelot (não ingress plugins)

### Observability

- **Prometheus**: ServiceMonitor + PodMonitor
- **Metrics path**: `/metrics`
- **Scrape interval**: 30s

---

## 🔍 Diferenças vs. Identity Service

### Similaridades
✅ Estrutura de pastas k8s/production/  
✅ GitHub Actions com OIDC  
✅ Multi-stage Dockerfile Alpine  
✅ HPA com scaling behavior customizado  
✅ AWS Load Balancer Controller  
✅ Prometheus monitoring  

### Diferenças

| Aspecto | Identity Service | API Gateway |
|---------|------------------|-------------|
| **Namespace** | `agrosolutions-identity` | `agrosolutions-gateway` |
| **Replicas** | 2-5 | 3-10 |
| **Dependencies** | Keycloak DB, AWS (SQS/SNS/SES) | Identity Service, Keycloak |
| **Config** | Múltiplos ConfigMaps | 2 ConfigMaps (app + ocelot) |
| **Volumes** | Database PVCs | Apenas emptyDir (logs) |
| **External Services** | AWS SNS/SQS | Downstream microservices |
| **Rate Limiting** | 200/min | 1000/min (gateway) |
| **LoadBalancer** | Não | RoundRobin (Ocelot) |

---

## 📈 Próximos Passos Recomendados

### 1. Configurar Environments no GitHub

**Settings > Environments**

Criar:
- `production` com protection rules (required reviewers, wait timer)
- `staging` sem proteção

### 2. Configurar Notifications

Adicionar aos workflows:

```yaml
- name: Notify Slack on Failure
  if: failure()
  uses: 8398a7/action-slack@v3
  with:
    status: failure
    webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

### 3. Implementar Canary Deployments

Usar Flagger + Istio/Linkerd para deployments progressivos.

### 4. Adicionar Testes de Integração

No `build.yml`:
```yaml
- name: Integration Tests
  run: |
    docker-compose up -d
    dotnet test tests/Integration/
    docker-compose down
```

### 5. Configurar Backup/Disaster Recovery

Usar Velero para backup de configs:
```bash
velero backup create api-gateway-backup --include-namespaces agrosolutions-gateway
```

---

## 🆘 Comandos Úteis Rápidos

```bash
# Ver pods
kubectl get pods -n agrosolutions-gateway

# Logs em tempo real
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Port-forward para teste local
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway

# Ver HPA status
kubectl get hpa -n agrosolutions-gateway

# Escalar manualmente
kubectl scale deployment api-gateway --replicas=5 -n agrosolutions-gateway

# Rollback
kubectl rollout undo deployment/api-gateway -n agrosolutions-gateway

# Ver métricas
kubectl top pods -n agrosolutions-gateway

# Testar health
curl http://localhost:8080/health

# Ver config Ocelot
POD=$(kubectl get pods -n agrosolutions-gateway -l app=api-gateway -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n agrosolutions-gateway $POD -- cat /app/ocelot.json

# Testar conectividade com downstream
kubectl exec -n agrosolutions-gateway $POD -- \
  wget -qO- http://identity-api-service.agrosolutions-identity/health
```

---

## 📚 Documentação Completa

- **[k8s/production/README.md](../k8s/production/README.md)** - Guia de deployment Kubernetes
- **[k8s/production/COMMANDS.md](../k8s/production/COMMANDS.md)** - Lista completa de comandos
- **[docs/DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md)** - Setup AWS e deploy passo-a-passo
- **[docs/GITHUB_ACTIONS_SETUP.md](./GITHUB_ACTIONS_SETUP.md)** - Configuração CI/CD detalhada

---

## ✅ Checklist de Setup

### AWS
- [ ] ECR repository criado
- [ ] IAM OIDC provider criado
- [ ] IAM role para GitHub Actions criada
- [ ] Policies anexadas à role

### Kubernetes
- [ ] EKS cluster acessível via kubectl
- [ ] Identity Service deployado
- [ ] AWS Load Balancer Controller instalado
- [ ] aws-auth ConfigMap atualizado
- [ ] Secrets criados no namespace

### GitHub
- [ ] Secrets configurados (AWS_ROLE_TO_ASSUME, JWT_ISSUER)
- [ ] Workflows commitados em `.github/workflows/`
- [ ] Repositório GitHub permite Actions

### Deploy
- [ ] Namespace criado
- [ ] ConfigMaps aplicados
- [ ] Deployment rodando (3+ pods)
- [ ] Service criado
- [ ] Ingress configurado
- [ ] HPA ativo
- [ ] Health checks passando
- [ ] Métricas sendo coletadas

---

## 🎉 Conclusão

Você agora tem:

✅ **Kubernetes production-ready setup** com:
- Multi-replica deployment com anti-affinity
- Auto-scaling (HPA)
- Health checks configurados
- AWS ALB Ingress com TLS/SSL
- Prometheus monitoring

✅ **CI/CD completo** com GitHub Actions:
- Build automatizado com testes
- Security scanning (Trivy)
- Deploy automatizado para EKS
- OIDC authentication (sem AWS keys!)

✅ **Documentação completa**:
- Guias de setup e deployment
- Comandos úteis para operação
- Troubleshooting guides

**Pronto para produção! 🚀**

---

**Desenvolvido com ❤️ pela equipe AgroSolutions**
