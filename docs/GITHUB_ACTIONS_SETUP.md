# 🤖 GitHub Actions CI/CD Setup

Este documento explica como configurar e usar os workflows de CI/CD do GitHub Actions para o API Gateway.

## 📋 Índice

1. [Visão Geral](#visão-geral)
2. [Configuração Inicial](#configuração-inicial)
3. [Workflows Disponíveis](#workflows-disponíveis)
4. [Uso e Operação](#uso-e-operação)
5. [Troubleshooting](#troubleshooting)

---

## Visão Geral

### Workflows Criados

1. **`build.yml`** - Build, Test e Push para ECR
2. **`deploy.yml`** - Deploy para EKS

### Fluxo de CI/CD

```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│  Push Code  │ ───> │ Build & Test │ ───> │  Push ECR   │
└─────────────┘      └──────────────┘      └─────────────┘
                                                    │
                                                    ▼
                                            ┌──────────────┐
                                            │  Deploy EKS  │
                                            └──────────────┘
```

---

## Configuração Inicial

### 1. Configurar AWS OIDC Provider

**Apenas uma vez por conta AWS**:

```bash
# Criar OIDC provider
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
```

### 2. Criar IAM Role para GitHub Actions

```bash
# Substituir YOUR_GITHUB_ORG e YOUR_REPO
export GITHUB_ORG="YOUR_GITHUB_ORG"
export GITHUB_REPO="agrosolutions-api-gateway"
export AWS_ACCOUNT_ID="316295889438"  # Ou seu account ID

# Criar trust policy
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
          "token.actions.githubusercontent.com:sub": "repo:${GITHUB_ORG}/${GITHUB_REPO}:*"
        }
      }
    }
  ]
}
EOF

# Criar role
aws iam create-role \
  --role-name GitHubActionsAPIGateway \
  --assume-role-policy-document file://github-trust-policy.json \
  --description "Role for GitHub Actions to deploy API Gateway"

# Anotar o ARN da role criada
aws iam get-role --role-name GitHubActionsAPIGateway --query 'Role.Arn' --output text
```

### 3. Anexar Policies à Role

```bash
# ECR permissions
aws iam attach-role-policy \
  --role-name GitHubActionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEC2ContainerRegistryPowerUser

# EKS permissions
aws iam attach-role-policy \
  --role-name GitHubActionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEKSClusterPolicy

# Custom EKS deploy policy
cat > eks-deploy-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "eks:DescribeCluster",
        "eks:ListClusters",
        "eks:DescribeNodegroup",
        "eks:ListNodegroups"
      ],
      "Resource": "*"
    }
  ]
}
EOF

aws iam create-policy \
  --policy-name EKSDeployPolicy \
  --policy-document file://eks-deploy-policy.json

aws iam attach-role-policy \
  --role-name GitHubActionsAPIGateway \
  --policy-arn arn:aws:iam::${AWS_ACCOUNT_ID}:policy/EKSDeployPolicy
```

### 4. Configurar RBAC do Kubernetes

**Importante**: Dar permissões ao role no cluster EKS.

```bash
# Editar aws-auth ConfigMap
kubectl edit configmap aws-auth -n kube-system
```

Adicionar ao `mapRoles`:

```yaml
- rolearn: arn:aws:iam::316295889438:role/GitHubActionsAPIGateway
  username: github-actions
  groups:
    - system:masters  # Ou criar role customizada com menos permissões
```

### 5. Configurar GitHub Secrets

Vá em: **Settings > Secrets and variables > Actions**

Criar os seguintes secrets:

| Nome do Secret | Valor | Descrição |
|----------------|-------|-----------|
| `AWS_ROLE_TO_ASSUME` | `arn:aws:iam::316295889438:role/GitHubActionsAPIGateway` | ARN da role criada |
| `JWT_ISSUER` | `http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions` | Issuer JWT |

**Opcional** (se usar ambientes):
- `AWS_REGION` - `sa-east-1`
- `EKS_CLUSTER_NAME` - `agrosolutions-eks-cluster`

---

## Workflows Disponíveis

### 1. Build and Push to ECR (`build.yml`)

**Localização**: `.github/workflows/build.yml`

**Triggers**:
- Push em `main` ou `develop` que altere:
  - `src/**`
  - `Dockerfile`
  - `.github/workflows/build.yml`
- Pull requests para `main` ou `develop`

**O que faz**:
1. ✅ Executa testes (`dotnet test`)
2. 🏗️ Build da imagem Docker
3. 🔒 Security scan com Trivy
4. 📤 Push para ECR com tags:
   - `latest`
   - `{branch}-{git-sha}`

**Exemplo de execução manual**:

```bash
# Via git push
git add .
git commit -m "feat: nova funcionalidade"
git push origin main

# Workflow executará automaticamente
```

**Tags geradas**:
- `latest` - Sempre a última versão do main
- `main-a1b2c3d4` - Branch + 8 primeiros chars do commit SHA
- `develop-e5f6g7h8` - Para branch develop

### 2. Deploy to EKS (`deploy.yml`)

**Localização**: `.github/workflows/deploy.yml`

**Triggers**:
- Push em `main` que altere `k8s/production/**`
- Manual via `workflow_dispatch` (escolhe environment)

**O que faz**:
1. 🔐 Configura credenciais AWS (via OIDC)
2. ☸️ Conecta ao EKS cluster
3. 🔑 Cria/atualiza secrets
4. 📦 Aplica ConfigMaps
5. 🚀 Deploy do Deployment
6. 📈 Configura HPA
7. 🌐 Configura Ingress
8. ✅ Verifica deployment e health

**Uso Manual via GitHub UI**:

1. Vá em **Actions** tab
2. Selecione **Deploy to EKS**
3. Clique em **Run workflow**
4. Escolha:
   - **Branch**: `main`
   - **Environment**: `production` ou `staging`
5. Clique em **Run workflow**

**Uso Manual via GitHub CLI**:

```bash
# Instalar GitHub CLI
brew install gh  # macOS
# ou
sudo apt install gh  # Linux

# Autenticar
gh auth login

# Executar workflow
gh workflow run deploy.yml \
  -f environment=production

# Ver runs
gh run list --workflow=deploy.yml

# Ver logs do último run
gh run view --log
```

---

## Uso e Operação

### Cenário 1: Deploy de Nova Versão (Automático)

```bash
# 1. Fazer mudanças no código
vim src/AgroSolutions.ApiGateway/Program.cs

# 2. Commit e push
git add .
git commit -m "feat: adicionar novo middleware"
git push origin main

# 3. GitHub Actions executará automaticamente:
#    - build.yml: testa e faz push para ECR
#    - deploy.yml: NÃO executa (código != k8s)

# 4. Para deployar no cluster, atualizar deployment manualmente ou...
```

### Cenário 2: Deploy Manual de Versão Específica

```bash
# Já existe imagem no ECR: agrosolutions-api-gateway:main-a1b2c3d4

# 1. Atualizar image tag no deployment
vim k8s/production/deployment.yaml
# Alterar: image: ...ecr.../agrosolutions-api-gateway:main-a1b2c3d4

# 2. Commit e push
git add k8s/production/deployment.yaml
git commit -m "chore: deploy version main-a1b2c3d4"
git push origin main

# 3. GitHub Actions executará deploy.yml automaticamente
```

### Cenário 3: Atualizar Configuração Ocelot

```bash
# 1. Editar ocelot config
vim k8s/production/configmaps.yaml
# Alterar rotas, rate limits, etc.

# 2. Commit e push
git add k8s/production/configmaps.yaml
git commit -m "config: atualizar rate limits"
git push origin main

# 3. deploy.yml executará automaticamente e:
#    - Aplicará novo ConfigMap
#    - Fará restart do deployment
```

### Cenário 4: Deploy Manual (Emergency)

```bash
# Executar deploy manualmente via UI ou CLI
gh workflow run deploy.yml -f environment=production

# Ou via UI: Actions > Deploy to EKS > Run workflow
```

### Cenário 5: Rollback

**Opção 1: Via GitHub Actions**

```bash
# 1. Reverter commit que causou problema
git revert <commit-hash>
git push origin main

# 2. deploy.yml executará automaticamente
```

**Opção 2: Via kubectl (mais rápido)**

```bash
# Conectar ao cluster
aws eks update-kubeconfig --name agrosolutions-eks-cluster --region sa-east-1

# Ver histórico
kubectl rollout history deployment/api-gateway -n agrosolutions-gateway

# Rollback
kubectl rollout undo deployment/api-gateway -n agrosolutions-gateway

# Ou para revisão específica
kubectl rollout undo deployment/api-gateway -n agrosolutions-gateway --to-revision=3
```

---

## Monitoramento de Workflows

### Ver Status via GitHub UI

1. Vá em **Actions** tab
2. Veja lista de runs recentes
3. Clique em um run para ver detalhes
4. Veja cada step (logs coloridos)

### Via GitHub CLI

```bash
# Listar workflows
gh workflow list

# Ver runs recentes
gh run list --limit 10

# Ver run específico
gh run view <run-id>

# Ver logs
gh run view <run-id> --log

# Reexecutar run que falhou
gh run rerun <run-id>
```

### Notificações

Configure notificações em:
**Settings > Notifications > Actions**

Opções:
- ✅ Only failed workflows
- ✅ Email notifications
- ✅ Web notifications

---

## Troubleshooting

### Erro: "Unable to assume role"

**Causa**: OIDC trust relationship mal configurado

**Solução**:

```bash
# Verificar trust policy da role
aws iam get-role --role-name GitHubActionsAPIGateway \
  --query 'Role.AssumeRolePolicyDocument'

# Trust policy deve ter:
# - Federated: arn:aws:iam::...:oidc-provider/token.actions.githubusercontent.com
# - Condition StringLike: repo:YOUR_ORG/agrosolutions-api-gateway:*
```

### Erro: "Error from server (Forbidden)"

**Causa**: Role não tem permissões no cluster EKS

**Solução**:

```bash
# Editar aws-auth
kubectl edit configmap aws-auth -n kube-system

# Adicionar role ao mapRoles (ver seção 4 acima)
```

### Erro: "ECR repository not found"

**Causa**: Repositório não foi criado

**Solução**:

```bash
aws ecr create-repository \
  --repository-name agrosolutions-api-gateway \
  --region sa-east-1
```

### Build falha em testes

**Causa**: Testes falhando

**Solução**:

```bash
# Rodar testes localmente
dotnet test

# Verificar o que está falhando e corrigir
```

### Deploy falha no health check

**Causa**: Pods não ficam prontos a tempo

**Solução**:

1. Verificar logs dos pods:
```bash
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway
```

2. Verificar events:
```bash
kubectl get events -n agrosolutions-gateway --sort-by='.lastTimestamp'
```

3. Aumentar timeout no workflow (se necessário):
```yaml
# Em deploy.yml
kubectl wait --for=condition=ready pod \
  -l app=api-gateway \
  -n agrosolutions-gateway \
  --timeout=600s  # Era 300s
```

---

## Best Practices

### 1. Usar Environments

Crie environments no GitHub:
- **Settings > Environments**
- Criar: `production`, `staging`
- Configurar protection rules:
  - ✅ Required reviewers (production)
  - ✅ Wait timer (5 minutes)

### 2. Versionamento Semântico

```bash
# Tag releases
git tag -a v1.2.0 -m "Release 1.2.0"
git push origin v1.2.0

# Atualizar workflow para usar tags
# build.yml: adicionar trigger on.push.tags
```

### 3. Múltiplos Ambientes

Estrutura de pastas:

```
k8s/
  staging/
    deployment.yaml    # 1 replica, menos recursos
  production/
    deployment.yaml    # 3 replicas, mais recursos
```

Workflow:
```yaml
# deploy.yml
- name: Apply manifests
  run: |
    kubectl apply -f k8s/${{ github.event.inputs.environment }}/
```

### 4. Notifications

Adicionar step de notificação:

```yaml
- name: Notify Slack
  if: failure()
  uses: 8398a7/action-slack@v3
  with:
    status: ${{ job.status }}
    webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

---

## 📚 Referências

- [GitHub Actions OIDC](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services)
- [AWS IAM OIDC Provider](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html)
- [EKS aws-auth ConfigMap](https://docs.aws.amazon.com/eks/latest/userguide/add-user-role.html)
- [GitHub CLI](https://cli.github.com/)

---

**Desenvolvido com ❤️ pela equipe AgroSolutions**
