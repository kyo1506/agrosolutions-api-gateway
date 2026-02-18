# 🚀 AgroSolutions API Gateway - Deployment Guide

Este guia completo cobre o setup e deployment do API Gateway na AWS EKS.

## 📋 Índice

1. [Pré-requisitos](#1-pré-requisitos)
2. [Configuração AWS](#2-configuração-aws)
3. [Setup do ECR](#3-setup-do-ecr)
4. [Build e Push da Imagem](#4-build-e-push-da-imagem)
5. [Configuração EKS](#5-configuração-eks)
6. [Deploy Kubernetes](#6-deploy-kubernetes)
7. [Configuração AWS Load Balancer](#7-configuração-aws-load-balancer)
8. [Verificação e Testes](#8-verificação-e-testes)
9. [Monitoramento](#9-monitoramento)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. Pré-requisitos

### Ferramentas Necessárias

```bash
# Verificar instalação
aws --version          # AWS CLI 2.x
kubectl version        # kubectl 1.28+
docker --version       # Docker 20.x+
helm version           # Helm 3.12+
```

### Instalar Ferramentas (se necessário)

#### AWS CLI
```bash
# Linux
curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
unzip awscliv2.zip
sudo ./aws/install

# Verificar
aws --version
```

#### kubectl
```bash
# Linux
curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
chmod +x kubectl
sudo mv kubectl /usr/local/bin/

# Verificar
kubectl version --client
```

#### Helm
```bash
curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
helm version
```

### Recursos AWS Necessários

- ✅ **AWS Account ID**: `316295889438` (ou seu account ID)
- ✅ **Region**: `sa-east-1` (São Paulo)
- ✅ **EKS Cluster**: `agrosolutions-eks-cluster`
- ✅ **VPC e Subnets**: Configuradas para EKS
- ✅ **IAM Roles**: Para EKS nodes e OIDC

### Serviços Prerequisitos

- ✅ **Identity Service** deployado (`agrosolutions-identity` namespace)
- ✅ **Keycloak** rodando (`agrosolutions-identity` namespace)
- ✅ **AWS Load Balancer Controller** instalado

---

## 2. Configuração AWS

### 2.1 Configurar Credenciais

```bash
# Configurar AWS CLI
aws configure

# Verificar identidade
aws sts get-caller-identity
```

**Saída esperada**:
```json
{
    "UserId": "AIDAXXXXXXXXXXXXXXXXX",
    "Account": "316295889438",
    "Arn": "arn:aws:iam::316295889438:user/your-user"
}
```

### 2.2 Criar IAM Role para GitHub Actions (OIDC)

```bash
# 1. Criar Identity Provider (uma vez por conta AWS)
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1

# 2. Criar trust policy
cat > github-actions-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::316295889438:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:YOUR_GITHUB_ORG/agrosolutions-api-gateway:*"
        }
      }
    }
  ]
}
EOF

# 3. Criar role
aws iam create-role \
  --role-name GitHubActionsAgroSolutionsAPIGateway \
  --assume-role-policy-document file://github-actions-trust-policy.json

# 4. Anexar policies necessárias
aws iam attach-role-policy \
  --role-name GitHubActionsAgroSolutionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEC2ContainerRegistryPowerUser

aws iam attach-role-policy \
  --role-name GitHubActionsAgroSolutionsAPIGateway \
  --policy-arn arn:aws:iam::aws:policy/AmazonEKSClusterPolicy
```

**⚠️ Importante**: Substitua `YOUR_GITHUB_ORG` pela sua organização/usuário do GitHub.

### 2.3 Criar Custom Policy para EKS

```bash
cat > eks-deploy-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "eks:DescribeCluster",
        "eks:ListClusters",
        "eks:DescribeNodegroup"
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
  --role-name GitHubActionsAgroSolutionsAPIGateway \
  --policy-arn arn:aws:iam::316295889438:policy/EKSDeployPolicy
```

---

## 3. Setup do ECR

### 3.1 Criar Repositório ECR

```bash
# Criar repositório
aws ecr create-repository \
  --repository-name agrosolutions-api-gateway \
  --region sa-east-1 \
  --image-scanning-configuration scanOnPush=true \
  --encryption-configuration encryptionType=AES256

# Configurar lifecycle policy (limpar imagens antigas)
cat > ecr-lifecycle-policy.json <<EOF
{
  "rules": [
    {
      "rulePriority": 1,
      "description": "Keep last 10 images",
      "selection": {
        "tagStatus": "any",
        "countType": "imageCountMoreThan",
        "countNumber": 10
      },
      "action": {
        "type": "expire"
      }
    }
  ]
}
EOF

aws ecr put-lifecycle-policy \
  --repository-name agrosolutions-api-gateway \
  --lifecycle-policy-text file://ecr-lifecycle-policy.json
```

### 3.2 Verificar Repositório

```bash
aws ecr describe-repositories \
  --repository-names agrosolutions-api-gateway \
  --region sa-east-1
```

---

## 4. Build e Push da Imagem

### 4.1 Login no ECR

```bash
# Fazer login
aws ecr get-login-password --region sa-east-1 | \
  docker login --username AWS --password-stdin 316295889438.dkr.ecr.sa-east-1.amazonaws.com

# Verificar login
docker info | grep -i registry
```

### 4.2 Build da Imagem

```bash
# Definir variáveis
export BUILD_DATE=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
export VERSION="1.0.0"
export REVISION=$(git rev-parse --short HEAD)
export ECR_REGISTRY="316295889438.dkr.ecr.sa-east-1.amazonaws.com"
export IMAGE_NAME="agrosolutions-api-gateway"

# Build
docker build \
  --build-arg BUILD_DATE=$BUILD_DATE \
  --build-arg VERSION=$VERSION \
  --build-arg REVISION=$REVISION \
  -t $ECR_REGISTRY/$IMAGE_NAME:latest \
  -t $ECR_REGISTRY/$IMAGE_NAME:$VERSION \
  -t $ECR_REGISTRY/$IMAGE_NAME:$REVISION \
  .

# Verificar imagem
docker images | grep api-gateway
```

### 4.3 Scan de Segurança (Opcional mas Recomendado)

```bash
# Instalar Trivy
curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin

# Scan da imagem
trivy image $ECR_REGISTRY/$IMAGE_NAME:latest
```

### 4.4 Push para ECR

```bash
# Push todas as tags
docker push $ECR_REGISTRY/$IMAGE_NAME:latest
docker push $ECR_REGISTRY/$IMAGE_NAME:$VERSION
docker push $ECR_REGISTRY/$IMAGE_NAME:$REVISION

# Verificar no ECR
aws ecr describe-images \
  --repository-name agrosolutions-api-gateway \
  --region sa-east-1
```

---

## 5. Configuração EKS

### 5.1 Conectar ao Cluster

```bash
# Atualizar kubeconfig
aws eks update-kubeconfig \
  --name agrosolutions-eks-cluster \
  --region sa-east-1

# Verificar conexão
kubectl cluster-info
kubectl get nodes
```

### 5.2 Verificar Pré-requisitos

```bash
# Verificar Identity Service
kubectl get pods -n agrosolutions-identity
kubectl get svc -n agrosolutions-identity

# Verificar AWS Load Balancer Controller
kubectl get deployment -n kube-system aws-load-balancer-controller
```

### 5.3 Instalar AWS Load Balancer Controller (se necessário)

```bash
# Criar IAM Policy
curl -o iam-policy.json https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/v2.7.0/docs/install/iam_policy.json

aws iam create-policy \
  --policy-name AWSLoadBalancerControllerIAMPolicy \
  --policy-document file://iam-policy.json

# Criar IAM Role via eksctl
eksctl create iamserviceaccount \
  --cluster=agrosolutions-eks-cluster \
  --namespace=kube-system \
  --name=aws-load-balancer-controller \
  --attach-policy-arn=arn:aws:iam::316295889438:policy/AWSLoadBalancerControllerIAMPolicy \
  --override-existing-serviceaccounts \
  --approve

# Instalar via Helm
helm repo add eks https://aws.github.io/eks-charts
helm repo update

helm install aws-load-balancer-controller eks/aws-load-balancer-controller \
  -n kube-system \
  --set clusterName=agrosolutions-eks-cluster \
  --set serviceAccount.create=false \
  --set serviceAccount.name=aws-load-balancer-controller

# Verificar instalação
kubectl get deployment -n kube-system aws-load-balancer-controller
```

---

## 6. Deploy Kubernetes

### 6.1 Criar Namespace e Secrets

```bash
# Criar namespace
kubectl apply -f k8s/production/namespace.yaml

# Criar secrets (se necessário)
kubectl create secret generic jwt-secrets \
  --from-literal=issuer='http://keycloak-service.agrosolutions-identity:8080/realms/agrosolutions' \
  -n agrosolutions-gateway \
  --dry-run=client -o yaml | kubectl apply -f -
```

### 6.2 Deploy Completo

```bash
# ConfigMaps (inclui Ocelot config)
kubectl apply -f k8s/production/configmaps.yaml

# Services
kubectl apply -f k8s/production/services.yaml

# Deployment
kubectl apply -f k8s/production/deployment.yaml

# Aguardar pods ficarem prontos
kubectl wait --for=condition=ready pod \
  -l app=api-gateway \
  -n agrosolutions-gateway \
  --timeout=300s

# HPA (Auto-scaling)
kubectl apply -f k8s/production/hpa.yaml

# Ingress
kubectl apply -f k8s/production/ingress-aws.yaml

# Observability
kubectl apply -f k8s/production/observability.yaml
```

### 6.3 Verificar Deploy

```bash
# Pods
kubectl get pods -n agrosolutions-gateway

# Services
kubectl get svc -n agrosolutions-gateway

# Deployment
kubectl get deployment -n agrosolutions-gateway

# HPA
kubectl get hpa -n agrosolutions-gateway

# Ingress
kubectl get ingress -n agrosolutions-gateway
```

---

## 7. Configuração AWS Load Balancer

### 7.1 Obter ALB DNS Name

```bash
# Obter DNS do Application Load Balancer criado pelo Ingress
kubectl get ingress api-gateway-ingress -n agrosolutions-gateway

# Anotar o ADDRESS (DNS do ALB)
# Exemplo: k8s-agrosolu-apigat-xxx-yyy.sa-east-1.elb.amazonaws.com
```

### 7.2 Configurar DNS

Configure seu DNS para apontar para o ALB:

```
api.agrosolutions.com → <ALB_DNS_NAME>
```

**AWS Route53 Exemplo**:
```bash
ALB_DNS=$(kubectl get ingress api-gateway-ingress -n agrosolutions-gateway -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')

aws route53 change-resource-record-sets \
  --hosted-zone-id Z1234567890ABC \
  --change-batch '{
    "Changes": [{
      "Action": "UPSERT",
      "ResourceRecordSet": {
        "Name": "api.agrosolutions.com",
        "Type": "CNAME",
        "TTL": 300,
        "ResourceRecords": [{"Value": "'$ALB_DNS'"}]
      }
    }]
  }'
```

### 7.3 Configurar Certificado SSL/TLS (Opcional)

```bash
# 1. Criar/importar certificado no ACM
aws acm request-certificate \
  --domain-name api.agrosolutions.com \
  --validation-method DNS \
  --region sa-east-1

# 2. Obter ARN do certificado
CERT_ARN=$(aws acm list-certificates --region sa-east-1 \
  --query 'CertificateSummaryList[?DomainName==`api.agrosolutions.com`].CertificateArn' \
  --output text)

# 3. Atualizar ingress-aws.yaml com annotation
kubectl annotate ingress api-gateway-ingress \
  -n agrosolutions-gateway \
  alb.ingress.kubernetes.io/certificate-arn=$CERT_ARN \
  --overwrite
```

### 7.4 Testar Acesso

```bash
# Via DNS (após configuração)
curl https://api.agrosolutions.com/health

# Via ALB DNS diretamente
ALB_DNS=$(kubectl get ingress api-gateway-ingress -n agrosolutions-gateway -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')
curl -H "Host: api.agrosolutions.com" http://$ALB_DNS/health
```

---

## 8. Verificação e Testes

### 8.1 Health Checks

```bash
# Port-forward para testar localmente
kubectl port-forward deployment/api-gateway 8080:80 -n agrosolutions-gateway &

# Testar endpoints
curl http://localhost:8080/health
curl http://localhost:8080/health/ready
curl http://localhost:8080/health/live
curl http://localhost:8080/metrics
```

### 8.2 Testar Roteamento

```bash
# Obter token JWT do Identity Service
TOKEN=$(curl -X POST http://api.agrosolutions.com/identity/v1/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"password"}' | jq -r '.data.accessToken')

# Testar rota autenticada
curl -H "Authorization: Bearer $TOKEN" \
  http://api.agrosolutions.com/identity/v1/users
```

### 8.3 Verificar Logs

```bash
# Logs em tempo real
kubectl logs -f deployment/api-gateway -n agrosolutions-gateway

# Verificar eventos
kubectl get events -n agrosolutions-gateway --sort-by='.lastTimestamp'
```

---

## 9. Monitoramento

### 9.1 Prometheus Metrics

```bash
# Port-forward Prometheus
kubectl port-forward svc/prometheus-server 9090:80 -n monitoring

# Acessar: http://localhost:9090

# Query exemplo: rate(http_requests_total[5m])
```

### 9.2 Grafana Dashboards

```bash
# Port-forward Grafana
kubectl port-forward svc/grafana 3000:80 -n monitoring

# Acessar: http://localhost:3000
# Login: admin / admin
```

**Dashboards Recomendados**:
- ASP.NET Core - ID: `10915`
- Kubernetes Pods - ID: `6417`
- NGINX Ingress - ID: `9614`

---

## 10. Troubleshooting

### Problema: Pods não iniciam

```bash
# Ver detalhes
kubectl describe pod <pod-name> -n agrosolutions-gateway

# Ver logs do container
kubectl logs <pod-name> -n agrosolutions-gateway

# Events
kubectl get events -n agrosolutions-gateway --sort-by='.lastTimestamp'
```

**Soluções**:
- Verificar se a imagem existe no ECR
- Verificar ConfigMap `ocelot-config`
- Verificar resources (CPU/Memory)

### Problema: Erro de autenticação JWT

```bash
# Verificar configuração JWT
kubectl get configmap api-gateway-config -n agrosolutions-gateway -o yaml | grep Jwt

# Testar conectividade com Keycloak
kubectl exec -it <pod-name> -n agrosolutions-gateway -- \
  wget -qO- http://keycloak-service.agrosolutions-identity:8080/health
```

### Problema: Routes retornam 502

```bash
# Verificar downstream services
kubectl get pods -n agrosolutions-identity
kubectl get svc -n agrosolutions-identity

# Testar conectividade
kubectl exec -it <pod-name> -n agrosolutions-gateway -- \
  wget -qO- http://identity-api-service.agrosolutions-identity/health
```

---

## ✅ Checklist de Deploy

- [ ] AWS CLI configurado
- [ ] kubectl configurado com acesso ao EKS
- [ ] ECR repository criado
- [ ] Imagem Docker built e pushed
- [ ] AWS Load Balancer Controller instalado
- [ ] Namespace criado
- [ ] Secrets criados
- [ ] ConfigMaps aplicados
- [ ] Deployment aplicado e pods rodando
- [ ] Service criado
- [ ] Ingress configurado
- [ ] DNS apontando para ALB
- [ ] ACM Certificate configurado
- [ ] Health checks passando
- [ ] Logs sem erros
- [ ] Métricas sendo coletadas

---

## 📚 Próximos Passos

1. **Configurar CI/CD** - Veja [GitHub Actions Setup](#github-actions-setup)
2. **Setup Monitoramento** - Prometheus + Grafana
3. **Configurar Alertas** - PagerDuty, Slack, etc.
4. **Implementar Backups** - Velero para backup de configs
5. **Configurar Auto-scaling** - Cluster Autoscaler

---

## 🔗 Links Úteis

- [AWS EKS Documentation](https://docs.aws.amazon.com/eks/)
- [Ocelot Documentation](https://ocelot.readthedocs.io/)
- [AWS Load Balancer Controller](https://kubernetes-sigs.github.io/aws-load-balancer-controller/)
- [AWS Certificate Manager](https://docs.aws.amazon.com/acm/)
- [k8s/production/README.md](../k8s/production/README.md)
- [k8s/production/COMMANDS.md](../k8s/production/COMMANDS.md)

---

**Desenvolvido com ❤️ pela equipe AgroSolutions**
