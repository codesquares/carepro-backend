# Docker and AWS Deployment Guide for CarePro Backend

## 🐳 Docker Configuration

The CarePro backend is now fully containerized and ready for deployment on AWS or any Docker-compatible platform.

### 📁 Docker Files Overview

- **`Dockerfile`** - Multi-stage build for optimized production image
- **`docker-compose.yml`** - Development environment with MongoDB and Redis
- **`docker-compose.prod.yml`** - Production environment configuration
- **`.dockerignore`** - Files excluded from Docker build context
- **`.env.example`** - Environment variables template
- **`docker-deploy.sh`** - Automation script for Docker operations

### 🚀 Quick Start

1. **Development Environment**:
   ```bash
   # Copy environment template
   cp .env.example .env
   # Edit .env with your values
   
   # Start development environment
   ./docker-deploy.sh dev
   ```

2. **Production Build**:
   ```bash
   # Build production image
   ./docker-deploy.sh build
   
   # Start production environment
   ./docker-deploy.sh prod
   ```

### 🔧 Available Docker Commands

```bash
./docker-deploy.sh build      # Build Docker image
./docker-deploy.sh dev        # Start development environment
./docker-deploy.sh prod       # Start production environment
./docker-deploy.sh test       # Run tests in container
./docker-deploy.sh logs       # Show container logs
./docker-deploy.sh stop       # Stop all services
./docker-deploy.sh clean      # Clean unused Docker resources
```

## ☁️ AWS Deployment

### 📋 Prerequisites

1. **AWS CLI** installed and configured
2. **Docker** installed locally
3. **IAM permissions** for:
   - ECR (Elastic Container Registry)
   - ECS (Elastic Container Service)
   - VPC and networking resources
   - Secrets Manager
   - CloudWatch Logs

### 🏗️ AWS Infrastructure Components

- **ECR Repository** - Container image storage
- **ECS Fargate Cluster** - Serverless container hosting
- **Application Load Balancer** - Traffic distribution and SSL termination
- **VPC with subnets** - Network isolation
- **Security Groups** - Firewall rules
- **Secrets Manager** - Secure configuration storage
- **CloudWatch** - Logging and monitoring

### 🚀 AWS Deployment Steps

1. **Configure AWS Credentials**:
   ```bash
   aws configure
   # Enter your AWS Access Key ID, Secret, and region
   ```

2. **Set Environment Variables**:
   ```bash
   export AWS_REGION=us-east-1
   export AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
   ```

3. **Deploy to AWS**:
   ```bash
   # Full deployment pipeline
   ./aws/deploy.sh deploy
   
   # Or step by step:
   ./aws/deploy.sh build    # Build and push to ECR
   ./aws/deploy.sh update   # Update ECS service
   ./aws/deploy.sh status   # Check deployment status
   ```

### 🔐 Secrets Management

Store sensitive configuration in AWS Secrets Manager:

```bash
# Create secrets (replace with actual values)
aws secretsmanager create-secret \
    --name "carepro/mongodb-connection" \
    --secret-string "mongodb://username:password@cluster.mongodb.net/carepro"

aws secretsmanager create-secret \
    --name "carepro/jwt-secret" \
    --secret-string "your-256-bit-secret-key"

aws secretsmanager create-secret \
    --name "carepro/openai-api-key" \
    --secret-string "your-openai-api-key"

aws secretsmanager create-secret \
    --name "carepro/google-maps-api-key" \
    --secret-string "your-google-maps-api-key"
```

### 🌐 Load Balancer Configuration

Create an Application Load Balancer:

```bash
# Create ALB
aws elbv2 create-load-balancer \
    --name carepro-alb \
    --subnets subnet-12345 subnet-67890 \
    --security-groups sg-12345

# Create target group
aws elbv2 create-target-group \
    --name carepro-targets \
    --protocol HTTP \
    --port 8080 \
    --vpc-id vpc-12345 \
    --target-type ip \
    --health-check-path /health
```

### 📊 Monitoring and Logging

- **CloudWatch Logs** - Application logs in `/ecs/carepro-backend`
- **CloudWatch Metrics** - Container and service metrics
- **Health Checks** - Automated health monitoring
- **Alarms** - Notifications for critical issues

## 🔒 Security Considerations

### 🛡️ Container Security

- **Non-root user** - Application runs as `carepro` user
- **Minimal base image** - Using official .NET runtime image
- **No sensitive data** - All secrets stored in environment variables
- **Health checks** - Automated container health monitoring

### 🌐 Network Security

- **VPC isolation** - Private subnets for containers
- **Security groups** - Restrictive firewall rules
- **ALB SSL termination** - HTTPS encryption
- **Internal communication** - Services communicate privately

### 🔐 Data Security

- **Secrets Manager** - Encrypted secret storage
- **IAM roles** - Least privilege access
- **Encryption in transit** - All API communication over HTTPS
- **Encryption at rest** - Database and storage encryption

## 📈 Scaling and Performance

### 🔄 Auto Scaling

Configure ECS service auto scaling:

```bash
# Create auto scaling target
aws application-autoscaling register-scalable-target \
    --service-namespace ecs \
    --resource-id service/carepro-cluster/carepro-backend-service \
    --scalable-dimension ecs:service:DesiredCount \
    --min-capacity 2 \
    --max-capacity 10

# Create scaling policy
aws application-autoscaling put-scaling-policy \
    --service-namespace ecs \
    --resource-id service/carepro-cluster/carepro-backend-service \
    --scalable-dimension ecs:service:DesiredCount \
    --policy-name carepro-cpu-scaling \
    --policy-type TargetTrackingScaling
```

### ⚡ Performance Optimization

- **Multi-stage Docker build** - Optimized image size
- **Resource limits** - Proper CPU and memory allocation
- **Connection pooling** - Efficient database connections
- **Caching** - Redis for performance optimization
- **CDN integration** - CloudFront for static assets

## 🔄 CI/CD Integration

The Docker configuration integrates with the GitHub Actions workflows:

- **Build workflow** - Automated Docker image building
- **Security scanning** - Container vulnerability scanning
- **Deployment workflow** - Automated AWS deployment
- **Rollback capability** - Easy rollback to previous versions

## 📋 Maintenance Tasks

### 🧹 Regular Maintenance

```bash
# Update base images
docker pull mcr.microsoft.com/dotnet/aspnet:8.0
./docker-deploy.sh build

# Clean up unused resources
./docker-deploy.sh clean
aws ecr list-images --repository-name carepro-backend --filter tagStatus=UNTAGGED

# Monitor logs
./aws/deploy.sh logs

# Check service health
./aws/deploy.sh status
```

### 🔄 Updates and Rollbacks

```bash
# Deploy new version
git tag v1.1.0
./aws/deploy.sh deploy

# Rollback if needed
aws ecs update-service \
    --cluster carepro-cluster \
    --service carepro-backend-service \
    --task-definition carepro-backend:PREVIOUS_REVISION
```

## 🆘 Troubleshooting

### 🔍 Common Issues

1. **Container won't start**:
   ```bash
   docker logs carepro-api
   ./aws/deploy.sh logs
   ```

2. **Database connection issues**:
   ```bash
   # Check connection string in secrets
   aws secretsmanager get-secret-value --secret-id carepro/mongodb-connection
   ```

3. **Service unhealthy**:
   ```bash
   # Check health endpoint
   curl http://localhost:8080/health
   ```

4. **Deployment failed**:
   ```bash
   # Check ECS service events
   aws ecs describe-services --cluster carepro-cluster --services carepro-backend-service
   ```

### 📞 Support Resources

- **AWS Documentation** - ECS, ECR, and Fargate guides
- **Docker Documentation** - Container best practices
- **GitHub Issues** - Project-specific support
- **AWS Support** - Technical assistance for AWS services

---

Your CarePro backend is now fully containerized and ready for enterprise-grade deployment on AWS! 🎉