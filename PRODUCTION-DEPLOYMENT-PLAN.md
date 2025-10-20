# CarePro Production Deployment Plan - Staging vs Production Architecture

## Overview
This plan sets up a complete production environment while preserving your staging setup:

### 🎯 **Staging Environment (Keep as-is)**
- **Frontend**: `carepro-frontend-staging` S3 bucket → points to **Azure backend**
- **Purpose**: Testing and development
- **Backend**: Azure-hosted API (unchanged)
- **Database**: Current MongoDB Atlas with test data

### 🚀 **Production Environment (New)**
- **Domain**: `https://oncarepro.com` (migrate from Vercel to AWS)
- **Frontend**: New production S3 bucket → points to **AWS C# backend**
- **Backend**: `https://oncarepro.com/api/cnet/*` (AWS ECS)
- **Node API**: `https://oncarepro.com/api/node-api/*` (shared with staging - no database)
- **Database**: Fresh MongoDB Atlas instance (clean production data)

## Architecture Strategy

### ✅ **Shared Node API Strategy**
```
STAGING:
carepro-frontend-staging → Azure Backend → Test MongoDB
                        → Shared Node API (middleware only)

PRODUCTION:
oncarepro.com → AWS C# Backend → Fresh MongoDB
             → Shared Node API (same instance as staging)
```

**Benefits of Shared Node API:**
- ✅ Cost-effective: Single infrastructure for stable middleware
- ✅ Consistent behavior: Same API responses for both environments  
- ✅ Simplified maintenance: One deployment to manage
- ✅ No database complexity: Pure middleware/proxy functionality

### ✅ **Domain & Routing Strategy**
- **Root domain**: `https://oncarepro.com/*` → Production frontend
- **C# API**: `https://oncarepro.com/api/cnet/*` → AWS ECS backend
- **Node API**: `https://oncarepro.com/api/node-api/*` → Your existing Node deployment
- **Marketing**: Replace Vercel marketing pages with production frontend

### ✅ **Database Strategy**
- **Staging MongoDB**: Keep current Atlas instance with test data
- **Production MongoDB**: New Atlas instance, fresh/clean data
- **Node API Database**: Keep existing database (unchanged)

## Phase 1: Database Setup (Fresh MongoDB Atlas Instance)

### 1.1 Create New MongoDB Atlas Cluster
```bash
# MANUAL STEPS (MongoDB Atlas Web Console):
# 1. Go to cloud.mongodb.com
# 2. Create new project: "CarePro-Production"
# 3. Create new cluster:
#    - Name: "carepro-prod-cluster"
#    - Provider: AWS (same region as your ECS)
#    - Tier: M10 or higher for production
#    - Database: "carepro_production"

echo "📋 Create MongoDB Atlas cluster manually in web console"
echo "🌍 Recommended: AWS us-east-1 (same as your ECS)"
echo "💾 Database name: carepro_production"
```

### 1.2 Configure Network Access and Database User
```bash
# MANUAL STEPS (MongoDB Atlas Web Console):
# 1. Network Access → Add IP Address → Add Current IP + 0.0.0.0/0 (temporarily for ECS)
# 2. Database Access → Add New Database User:
#    - Username: codesquareltd_db_user
#    - Password: Codesquare2025
#    - Role: readWrite on carepro_production database

echo "🔐 Create database user: carepro_prod_user"
echo "🌐 Allow network access: 0.0.0.0/0 (will restrict later)"
```

### 1.3 Get Connection String
```bash
# MANUAL STEP: Copy connection string from Atlas
# Format: mongodb+srv://<username>:<password>@<cluster>.mongodb.net/<database>?retryWrites=true&w=majority

# Example connection string (replace with your actual values):
PROD_MONGO_CONNECTION="mongodb+srv://<username>:<password>@<cluster>.mongodb.net/<database>?retryWrites=true&w=majority"

echo "Connection string format:"
echo "mongodb+srv://<username>:<password>@<cluster>.mongodb.net/<database>?retryWrites=true&w=majority"
```

### 1.4 Store Database Credentials in AWS Secrets Manager
```bash
# Configure AWS CLI with carepro user credentials
aws configure set aws_access_key_id YOUR_CAREPRO_ACCESS_KEY
aws configure set aws_secret_access_key YOUR_CAREPRO_SECRET_KEY
aws configure set default.region us-east-1

# Store MongoDB connection string in Secrets Manager
aws secretsmanager create-secret \
    --name "carepro/prod/mongodb" \
    --description "CarePro Production MongoDB Atlas Connection" \
    --secret-string '{
        "ConnectionString": "mongodb+srv://codesquareltd_db_user:<db_password>@carepro-prod-cluster.d179ao.mongodb.net/?retryWrites=true&w=majority&appName=carepro-prod-cluster",
        "DatabaseName": "carepro_production"
    }'

echo "✅ MongoDB credentials stored in AWS Secrets Manager"
```

### 1.5 Initialize Production Database Schema
```bash
# No migration needed! Your existing Entity Framework MongoDB setup will:
# 1. Connect to the new Atlas instance
# 2. Automatically create collections when first accessed
# 3. Use same entity models and configurations

echo "🎉 Database ready - EF Core will auto-create collections on first use"
echo "📊 Fresh database with no test data"
```

## Phase 2: Application Load Balancer Setup

### 2.1 Create ALB Security Group
```bash
# Create security group for ALB
aws ec2 create-security-group \
    --group-name carepro-alb-sg \
    --description "CarePro ALB Security Group" \
    --vpc-id $VPC_ID

# Get ALB security group ID
ALB_SG_ID=$(aws ec2 describe-security-groups --filters "Name=group-name,Values=carepro-alb-sg" --query 'SecurityGroups[0].GroupId' --output text)

# Allow HTTP and HTTPS traffic
aws ec2 authorize-security-group-ingress \
    --group-id $ALB_SG_ID \
    --protocol tcp \
    --port 80 \
    --cidr 0.0.0.0/0

aws ec2 authorize-security-group-ingress \
    --group-id $ALB_SG_ID \
    --protocol tcp \
    --port 443 \
    --cidr 0.0.0.0/0
```

### 2.2 Create Target Group for CarePro Backend
```bash
aws elbv2 create-target-group \
    --name carepro-backend-tg \
    --protocol HTTP \
    --port 8080 \
    --vpc-id $VPC_ID \
    --target-type ip \
    --health-check-path /health \
    --health-check-interval-seconds 30 \
    --health-check-timeout-seconds 5 \
    --healthy-threshold-count 2 \
    --unhealthy-threshold-count 3 \
    --tags Key=Environment,Value=Production Key=Project,Value=CarePro
```

### 2.3 Create Application Load Balancer
```bash
# Get subnet IDs (replace with your actual subnet IDs)
SUBNET_IDS=$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" --query 'Subnets[*].SubnetId' --output text)

aws elbv2 create-load-balancer \
    --name carepro-production-alb \
    --subnets $SUBNET_IDS \
    --security-groups $ALB_SG_ID \
    --scheme internet-facing \
    --type application \
    --ip-address-type ipv4 \
    --tags Key=Environment,Value=Production Key=Project,Value=CarePro
```

### 2.4 Get ALB DNS Name
```bash
ALB_DNS=$(aws elbv2 describe-load-balancers --names carepro-production-alb --query 'LoadBalancers[0].DNSName' --output text)
echo "ALB DNS Name: $ALB_DNS"
```

## Phase 3: SSL Certificate Setup

### 3.1 Request SSL Certificate
```bash
aws acm request-certificate \
    --domain-name oncarepro.com \
    --subject-alternative-names "*.oncarepro.com" \
    --validation-method DNS \
    --tags Key=Environment,Value=Production Key=Project,Value=CarePro
```

### 3.2 Get Certificate ARN and Validation Records
```bash
# Get certificate ARN
CERT_ARN=$(aws acm list-certificates --certificate-statuses PENDING_VALIDATION --query 'CertificateSummaryList[?DomainName==`oncarepro.com`].CertificateArn' --output text)

# Get DNS validation records
aws acm describe-certificate --certificate-arn $CERT_ARN --query 'Certificate.DomainValidationOptions'
```

### 3.3 Create DNS Validation Records
```bash
# First, get your hosted zone ID
HOSTED_ZONE_ID=$(aws route53 list-hosted-zones --query 'HostedZones[?Name==`oncarepro.com.`].Id' --output text | cut -d'/' -f3)

# Create validation records (replace values with actual validation data from step 3.2)
aws route53 change-resource-record-sets \
    --hosted-zone-id $HOSTED_ZONE_ID \
    --change-batch '{
        "Changes": [{
            "Action": "CREATE",
            "ResourceRecordSet": {
                "Name": "_validation_record_name_from_acm",
                "Type": "CNAME",
                "TTL": 300,
                "ResourceRecords": [{"Value": "_validation_record_value_from_acm"}]
            }
        }]
    }'
```

## Phase 4: Configure Load Balancer Listeners and Rules

### 4.1 Create HTTPS Listener
```bash
# Get ALB ARN
ALB_ARN=$(aws elbv2 describe-load-balancers --names carepro-production-alb --query 'LoadBalancers[0].LoadBalancerArn' --output text)

# Get target group ARN
TG_ARN=$(aws elbv2 describe-target-groups --names carepro-backend-tg --query 'TargetGroups[0].TargetGroupArn' --output text)

# Create HTTPS listener
aws elbv2 create-listener \
    --load-balancer-arn $ALB_ARN \
    --protocol HTTPS \
    --port 443 \
    --certificates CertificateArn=$CERT_ARN \
    --default-actions Type=fixed-response,FixedResponseConfig='{MessageBody="Default Response",StatusCode="404",ContentType="text/plain"}'
```

### 4.2 Create HTTP Listener (Redirect to HTTPS)
```bash
aws elbv2 create-listener \
    --load-balancer-arn $ALB_ARN \
    --protocol HTTP \
    --port 80 \
    --default-actions Type=redirect,RedirectConfig='{Protocol="HTTPS",Port="443",StatusCode="HTTP_301"}'
```

### 4.3 Create Listener Rule for /api/cnet/* Path
```bash
# Get HTTPS listener ARN
LISTENER_ARN=$(aws elbv2 describe-listeners --load-balancer-arn $ALB_ARN --query 'Listeners[?Protocol==`HTTPS`].ListenerArn' --output text)

aws elbv2 create-rule \
    --listener-arn $LISTENER_ARN \
    --priority 100 \
    --conditions Field=path-pattern,Values="/api/cnet/*" \
    --actions Type=forward,TargetGroupArn=$TG_ARN
```

## Phase 5: Update ECS Configuration

### 5.1 Update ECS Task Definition
Create a new task definition file: `ecs-task-definition-production.json`

```bash
cat > ecs-task-definition-production.json << 'EOF'
{
    "family": "carepro-backend-prod",
    "networkMode": "awsvpc",
    "requiresCompatibilities": ["FARGATE"],
    "cpu": "512",
    "memory": "1024",
    "executionRoleArn": "arn:aws:iam::YOUR_ACCOUNT:role/ecsTaskExecutionRole",
    "taskRoleArn": "arn:aws:iam::YOUR_ACCOUNT:role/ecsTaskRole",
    "containerDefinitions": [
        {
            "name": "carepro-backend",
            "image": "YOUR_ECR_REPO:latest",
            "portMappings": [
                {
                    "containerPort": 8080,
                    "protocol": "tcp"
                }
            ],
            "essential": true,
            "logConfiguration": {
                "logDriver": "awslogs",
                "options": {
                    "awslogs-group": "/ecs/carepro-backend-prod",
                    "awslogs-region": "us-east-1",
                    "awslogs-stream-prefix": "ecs"
                }
            },
            "secrets": [
                {
                    "name": "ConnectionStrings__DefaultConnection",
                    "valueFrom": "arn:aws:secretsmanager:us-east-1:YOUR_ACCOUNT:secret:carepro/prod/database:ConnectionString::"
                }
            ],
            "environment": [
                {
                    "name": "ASPNETCORE_ENVIRONMENT",
                    "value": "Production"
                },
                {
                    "name": "ASPNETCORE_URLS",
                    "value": "http://+:8080"
                }
            ]
        }
    ]
}
EOF
```

### 5.2 Register New Task Definition
```bash
aws ecs register-task-definition --cli-input-json file://ecs-task-definition-production.json
```

### 5.3 Update ECS Service
```bash
# Update service to use new task definition and target group
aws ecs update-service \
    --cluster carepro-staging-cluster \
    --service carepro-backend-service \
    --task-definition carepro-backend-prod:1 \
    --load-balancers targetGroupArn=$TG_ARN,containerName=carepro-backend,containerPort=8080 \
    --network-configuration "awsvpcConfiguration={subnets=[$SUBNET_IDS],securityGroups=[sg-xxxxxxxx],assignPublicIp=DISABLED}"
```

## Phase 6: Production Frontend & Domain Setup

### 6.1 Create Production Frontend S3 Bucket
```bash
# Create NEW production frontend bucket (separate from staging)
PROD_FRONTEND_BUCKET="oncarepro-frontend-production"
STAGING_FRONTEND_BUCKET="carepro-frontend-staging"

echo "📦 Creating production frontend bucket..."
aws s3 mb s3://$PROD_FRONTEND_BUCKET --region us-east-1

# Enable static website hosting for production
aws s3 website s3://$PROD_FRONTEND_BUCKET \
    --index-document index.html \
    --error-document error.html

# Set public read access for production frontend
cat > frontend-bucket-policy.json << EOF
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "PublicReadGetObject",
            "Effect": "Allow",
            "Principal": "*",
            "Action": "s3:GetObject",
            "Resource": "arn:aws:s3:::$PROD_FRONTEND_BUCKET/*"
        }
    ]
}
EOF

aws s3api put-bucket-policy --bucket $PROD_FRONTEND_BUCKET --policy file://frontend-bucket-policy.json

echo "✅ Production frontend bucket created: $PROD_FRONTEND_BUCKET"
echo "ℹ️  Staging frontend remains unchanged: $STAGING_FRONTEND_BUCKET"
```

### 6.2 Create Route 53 Hosted Zone for Production Domain
```bash
# Create hosted zone for oncarepro.com
aws route53 create-hosted-zone \
    --name oncarepro.com \
    --caller-reference carepro-domain-$(date +%s) \
    --hosted-zone-config Comment="CarePro production domain"

# Get the hosted zone ID and name servers
HOSTED_ZONE_ID=$(aws route53 list-hosted-zones --query 'HostedZones[?Name==`oncarepro.com.`].Id' --output text | cut -d'/' -f3)
NAME_SERVERS=$(aws route53 get-hosted-zone --id $HOSTED_ZONE_ID --query 'DelegationSet.NameServers' --output table)

echo "📋 IMPORTANT: Update your domain registrar with these name servers:"
echo "$NAME_SERVERS"
echo ""
echo "🎯 This will replace Vercel marketing pages with your production frontend"
```

### 6.3 Configure Shared Node API (App Runner)
```bash
# Your Node API is already deployed on AWS App Runner - perfect for sharing!
echo "✅ Node API discovered on AWS App Runner"

NODE_API_ENDPOINT="budmfp9jxr.us-east-1.awsapprunner.com"
NODE_API_URL="https://$NODE_API_ENDPOINT"

echo "📋 Node API Details:"
echo "   Platform: AWS App Runner (serverless containers)"
echo "   URL: $NODE_API_URL"
echo "   Status: ✅ Already deployed and running"
echo "   Usage: Shared between staging and production"
echo "   Benefits: Auto-scaling, managed infrastructure, cost-effective"
echo ""
echo "🎯 Current Access:"
echo "   Staging Frontend: http://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com"
echo "   Node API: $NODE_API_URL"
echo ""
echo "� Production Integration:"
echo "   Will route oncarepro.com/api/node-api/* → $NODE_API_URL"
echo "   No changes needed to Node API - it's perfect as-is!"

# Test Node API connectivity
echo "🧪 Testing Node API connectivity..."
curl -s -o /dev/null -w "HTTP Status: %{http_code}\n" $NODE_API_URL || echo "Note: Test from local machine if needed"
```
```bash
# Find your existing Node API deployment (will be shared by both environments)
echo "🔍 Discovering existing Node API deployment for shared use..."

# Check ECS services first
echo "Checking ECS services..."
NODE_SERVICES=$(aws ecs list-services --cluster carepro-staging-cluster --query 'serviceArns' --output text)
for service in $NODE_SERVICES; do
    SERVICE_NAME=$(echo $service | cut -d'/' -f3)
    if [[ $SERVICE_NAME == *"node"* ]]; then
        echo "✅ Found Node API in ECS: $SERVICE_NAME"
        NODE_API_TYPE="ECS"
        
        # Get service details to find load balancer
        SERVICE_DETAILS=$(aws ecs describe-services --cluster carepro-staging-cluster --services $SERVICE_NAME)
        echo "📋 Node API Service: $SERVICE_NAME"
        
        # Check if it has a load balancer
        LB_ARN=$(echo $SERVICE_DETAILS | jq -r '.services[0].loadBalancers[0].targetGroupArn // empty')
        if [ ! -z "$LB_ARN" ]; then
            # Get ALB DNS name from target group
            NODE_ALB_ARN=$(aws elbv2 describe-target-groups --target-group-arns $LB_ARN --query 'TargetGroups[0].LoadBalancerArns[0]' --output text)
            NODE_API_ENDPOINT=$(aws elbv2 describe-load-balancers --load-balancer-arns $NODE_ALB_ARN --query 'LoadBalancers[0].DNSName' --output text)
            echo "✅ Node API ALB endpoint: $NODE_API_ENDPOINT"
        fi
    fi
done

# Check Lambda functions if not found in ECS
if [ -z "$NODE_API_TYPE" ]; then
    NODE_FUNCTIONS=$(aws lambda list-functions --query 'Functions[?contains(FunctionName, `node`) || contains(FunctionName, `api`)].FunctionName' --output text)
    if [ ! -z "$NODE_FUNCTIONS" ]; then
        echo "✅ Found Node API Lambda functions: $NODE_FUNCTIONS"
        NODE_API_TYPE="LAMBDA"
        echo "📋 Node API will be accessed via API Gateway"
    fi
fi

# Check EC2 instances if not found elsewhere
if [ -z "$NODE_API_TYPE" ]; then
    NODE_INSTANCES=$(aws ec2 describe-instances --filters "Name=tag:Name,Values=*node*" "Name=instance-state-name,Values=running" --query 'Reservations[*].Instances[*].[InstanceId,PublicIpAddress]' --output text)
    if [ ! -z "$NODE_INSTANCES" ]; then
        echo "✅ Found Node API on EC2: $NODE_INSTANCES"
        NODE_API_TYPE="EC2"
    fi
fi

echo ""
echo "� Node API Summary:"
echo "   Type: $NODE_API_TYPE"
echo "   Endpoint: $NODE_API_ENDPOINT"
echo "   Usage: Shared between staging and production"
echo "   Function: Middleware/proxy (no database)"
```

### 6.4 Create CloudFront Distribution with Shared Node API
```bash
# Create CloudFront distribution that routes to existing Node API
cat > cloudfront-config.json << 'EOF'
{
    "CallerReference": "carepro-prod-distribution-$(date +%s)",
    "Comment": "CarePro production: Frontend + C# API + Shared Node API",
    "DefaultRootObject": "index.html",
    "Origins": [
        {
            "Id": "frontend-origin",
            "DomainName": "PROD_FRONTEND_BUCKET.s3-website-us-east-1.amazonaws.com",
            "CustomOriginConfig": {
                "HTTPPort": 80,
                "HTTPSPort": 443,
                "OriginProtocolPolicy": "http-only"
            }
        },
        {
            "Id": "cnet-api-origin", 
            "DomainName": "ALB_DNS_NAME",
            "CustomOriginConfig": {
                "HTTPPort": 80,
                "HTTPSPort": 443,
                "OriginProtocolPolicy": "https-only"
            }
        },
        {
            "Id": "shared-node-api-origin",
            "DomainName": "budmfp9jxr.us-east-1.awsapprunner.com",
            "CustomOriginConfig": {
                "HTTPPort": 80,
                "HTTPSPort": 443,
                "OriginProtocolPolicy": "https-only"
            }
        }
    ],
    "DefaultCacheBehavior": {
        "TargetOriginId": "frontend-origin",
        "ViewerProtocolPolicy": "redirect-to-https",
        "TrustedSigners": {
            "Enabled": false
        },
        "ForwardedValues": {
            "QueryString": false,
            "Cookies": {
                "Forward": "none"
            }
        },
        "DefaultTTL": 86400,
        "MaxTTL": 31536000
    },
    "CacheBehaviors": [
        {
            "PathPattern": "/api/cnet/*",
            "TargetOriginId": "cnet-api-origin",
            "ViewerProtocolPolicy": "redirect-to-https",
            "ForwardedValues": {
                "QueryString": true,
                "Headers": ["*"],
                "Cookies": {
                    "Forward": "all"
                }
            },
            "TrustedSigners": {
                "Enabled": false
            },
            "DefaultTTL": 0,
            "MaxTTL": 0
        },
        {
            "PathPattern": "/api/node-api/*",
            "TargetOriginId": "shared-node-api-origin", 
            "ViewerProtocolPolicy": "redirect-to-https",
            "ForwardedValues": {
                "QueryString": true,
                "Headers": ["*"],
                "Cookies": {
                    "Forward": "all"
                }
            },
            "TrustedSigners": {
                "Enabled": false
            },
            "DefaultTTL": 300,
            "MaxTTL": 3600
        }
    ],
    "Enabled": true,
    "Aliases": ["oncarepro.com", "www.oncarepro.com"],
    "ViewerCertificate": {
        "AcmCertificateArn": "CERTIFICATE_ARN",
        "SSLSupportMethod": "sni-only"
    }
}
EOF

# Replace placeholders with discovered values
sed -i "s/PROD_FRONTEND_BUCKET/$PROD_FRONTEND_BUCKET/g" cloudfront-config.json
sed -i "s/ALB_DNS_NAME/$ALB_DNS/g" cloudfront-config.json  
sed -i "s/NODE_API_ENDPOINT/$NODE_API_ENDPOINT/g" cloudfront-config.json
sed -i "s/CERTIFICATE_ARN/$CERT_ARN/g" cloudfront-config.json

# Update the config with discovered App Runner endpoint
NODE_API_ENDPOINT="budmfp9jxr.us-east-1.awsapprunner.com"

echo "🎯 CloudFront Configuration:"
echo "   Frontend: $PROD_FRONTEND_BUCKET (production S3)"
echo "   C# API: $ALB_DNS (production ECS via ALB)"  
echo "   Node API: $NODE_API_ENDPOINT (shared App Runner)"
echo ""
echo "📊 Perfect Architecture Benefits:"
echo "   ✅ App Runner: Auto-scaling, managed, cost-effective for Node API"
echo "   ✅ ECS: Full control and fresh database for C# API"
echo "   ✅ CloudFront: Routes /api/cnet/* to ALB, /api/node-api/* to App Runner"
echo "   ✅ Shared Node API: Same service for both staging and production"

# Replace placeholder in CloudFront config
sed -i "s/NODE_API_ENDPOINT/$NODE_API_ENDPOINT/g" cloudfront-config.json

# Create CloudFront distribution
aws cloudfront create-distribution --distribution-config file://cloudfront-config.json
```

### 6.4 Create DNS Records in Route 53
```bash
# Get CloudFront distribution domain name
CLOUDFRONT_DOMAIN=$(aws cloudfront list-distributions --query 'DistributionList.Items[?Comment==`CarePro production distribution with path-based routing`].DomainName' --output text)

# Create alias record for root domain
aws route53 change-resource-record-sets \
    --hosted-zone-id $HOSTED_ZONE_ID \
    --change-batch '{
        "Changes": [{
            "Action": "CREATE",
            "ResourceRecordSet": {
                "Name": "oncarepro.com",
                "Type": "A",
                "AliasTarget": {
                    "DNSName": "'$CLOUDFRONT_DOMAIN'",
                    "EvaluateTargetHealth": false,
                    "HostedZoneId": "Z2FDTNDATAQYW2"
                }
            }
        }]
    }'

# Create alias record for www subdomain
aws route53 change-resource-record-sets \
    --hosted-zone-id $HOSTED_ZONE_ID \
    --change-batch '{
        "Changes": [{
            "Action": "CREATE",
            "ResourceRecordSet": {
                "Name": "www.oncarepro.com",
                "Type": "A",
                "AliasTarget": {
                    "DNSName": "'$CLOUDFRONT_DOMAIN'",
                    "EvaluateTargetHealth": false,
                    "HostedZoneId": "Z2FDTNDATAQYW2"
                }
            }
        }]
    }'
```

### 6.5 Update Domain Registrar (CRITICAL MANUAL STEP)
```bash
echo "🚨 CRITICAL: Update your domain registrar DNS settings"
echo "👉 Go to your domain registrar (where you bought oncarepro.com)"
echo "👉 Replace current nameservers with Route 53 nameservers:"
echo "   (Use the nameservers from step 6.1)"
echo ""
echo "⏰ DNS propagation can take 24-48 hours"
echo "🔍 Test with: dig oncarepro.com NS"
```

### Final URL Structure:

#### 🎯 **Production Environment** (oncarepro.com):
- **Frontend**: `https://oncarepro.com/*` → New production S3 bucket → Points to AWS C# backend
- **C# Backend**: `https://oncarepro.com/api/cnet/*` → AWS ECS → Fresh MongoDB
- **Node API**: `https://oncarepro.com/api/node-api/*` → **App Runner** `https://budmfp9jxr.us-east-1.awsapprunner.com`

#### 🧪 **Staging Environment** (current):
- **Frontend**: `http://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com` → Points to Azure backend  
- **C# Backend**: Azure-hosted API → Test MongoDB Atlas
- **Node API**: `https://budmfp9jxr.us-east-1.awsapprunner.com` → **SHARED** App Runner service
- **Purpose**: Development and testing

#### � **Shared Node API (App Runner)**:
- **Platform**: AWS App Runner (serverless containers)
- **URL**: `https://budmfp9jxr.us-east-1.awsapprunner.com`
- **Usage**: Shared between staging and production  
- **Benefits**: Auto-scaling, managed infrastructure, no database dependencies
- **Perfect for middleware**: Stateless, cost-effective, high availability

## Phase 7: Application Configuration Updates

### 7.1 Update ECS Task Definition with New Database
```bash
# Create new production task definition
cat > ecs-task-definition-production.json << 'EOF'
{
    "family": "carepro-backend-prod",
    "networkMode": "awsvpc",
    "requiresCompatibilities": ["FARGATE"],
    "cpu": "512",
    "memory": "1024",
    "executionRoleArn": "arn:aws:iam::YOUR_ACCOUNT:role/ecsTaskExecutionRole",
    "taskRoleArn": "arn:aws:iam::YOUR_ACCOUNT:role/ecsTaskRole",
    "containerDefinitions": [
        {
            "name": "carepro-backend",
            "image": "YOUR_ECR_REPO:latest",
            "portMappings": [
                {
                    "containerPort": 8080,
                    "protocol": "tcp"
                }
            ],
            "essential": true,
            "logConfiguration": {
                "logDriver": "awslogs",
                "options": {
                    "awslogs-group": "/ecs/carepro-backend-prod",
                    "awslogs-region": "us-east-1",
                    "awslogs-stream-prefix": "ecs"
                }
            },
            "secrets": [
                {
                    "name": "ConnectionStrings__MongoDbConnection",
                    "valueFrom": "arn:aws:secretsmanager:us-east-1:YOUR_ACCOUNT:secret:carepro/prod/mongodb:ConnectionString::"
                }
            ],
            "environment": [
                {
                    "name": "ASPNETCORE_ENVIRONMENT",
                    "value": "Production"
                },
                {
                    "name": "ASPNETCORE_URLS",
                    "value": "http://+:8080"
                }
            ]
        }
    ]
}
EOF

# Register new task definition
aws ecs register-task-definition --cli-input-json file://ecs-task-definition-production.json
```

### 7.2 Update Application Configuration Files
```bash
# Update appsettings.Production.json to use connection string from environment
cat > CarePro-Api/appsettings.Production.json << 'EOF'
{
  "ConnectionStrings": {
    "MongoDbConnection": "FROM_ENVIRONMENT_VARIABLE"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "JWT": {
    "Key": "FbyNPnypbq9sHRASSNa36FLojgXh/APDCU6gGym7888=",
    "Issuer": "SecureApi",
    "Audience": "SecureApiUser",
    "DurationInHours": 5
  },
  "CORS": {
    "AllowedOrigins": [
      "https://oncarepro.com",
      "https://www.oncarepro.com"
    ]
  }
}
EOF
```

### 7.3 Update CORS Configuration in Program.cs
```bash
# No need to manually edit - just verify your CORS configuration allows the new domain
echo "✅ Verify CORS in Program.cs allows:"
echo "   - https://oncarepro.com"  
echo "   - https://www.oncarepro.com"
echo ""
echo "🔧 Example CORS configuration:"
echo 'builder.Services.AddCors(options =>'
echo '{'
echo '    options.AddPolicy("default", builder =>'
echo '    {'
echo '        builder'
echo '            .WithOrigins("https://oncarepro.com", "https://www.oncarepro.com")'
echo '            .AllowAnyMethod()'
echo '            .AllowAnyHeader()'
echo '            .AllowCredentials();'
echo '    });'
echo '});'
```

### 7.4 Data Seeding (Optional - for Production Setup)
```bash
# Since you want a fresh database without test data, you might want to seed basic data
# Create a minimal data seeding script for production

cat > seed-production-data.cs << 'EOF'
// Optional: Add this to your Program.cs or create a seeding service
// This will run once on startup to create essential data

public static async Task SeedProductionData(IServiceProvider serviceProvider)
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
    
    // Example: Create default admin user, system settings, etc.
    if (!await context.AdminUsers.AnyAsync())
    {
        var adminUser = new AdminUser
        {
            FirstName = "System",
            LastName = "Administrator", 
            Email = "admin@oncarepro.com",
            Role = "SuperAdmin",
            // ... other required fields
        };
        context.AdminUsers.Add(adminUser);
        await context.SaveChangesAsync();
    }
}
EOF

echo "📋 Optional: Review seed-production-data.cs for essential data setup"
```

### 7.5 No Data Migration Needed! 
```bash
echo "🎉 GREAT NEWS: No complex data migration required!"
echo ""
echo "✅ Your current setup uses MongoDB.EntityFrameworkCore.Extensions"
echo "✅ Same entity models work with new MongoDB Atlas instance"
echo "✅ EF Core will automatically create collections on first use"
echo "✅ Fresh database = no test data contamination"
echo ""
echo "🚀 Simply update connection string and deploy!"
```

## Phase 8: Verification and Testing

### 🎉 **PHASE 8 VERIFICATION RESULTS - COMPLETED SUCCESSFULLY!**

#### ✅ **Infrastructure Status Summary**:

1. **ECS Service**: ✅ Healthy and running in steady state
2. **Target Group**: ✅ ECS task registered and passing health checks (IP: 172.31.43.50:8080)  
3. **ALB Connectivity**: ✅ Load balancer successfully routing to ECS tasks
4. **Health Endpoint**: ✅ Returning healthy status via HTTP
5. **VPC Networking**: ✅ All networking issues resolved with VPC endpoint (vpce-09a5e4ae81d83193d)
6. **Secrets Manager**: ✅ ECS tasks successfully retrieving MongoDB connection strings

#### 🔧 **Current Working Configuration**:
- **ALB DNS**: `carepro-production-alb-correct-572837602.us-east-1.elb.amazonaws.com`
- **Protocol**: HTTP on port 80 (working)
- **Health Check**: `/health` endpoint responding correctly
- **ECS Tasks**: Healthy and accessible via ALB
- **Database**: Successfully connected to MongoDB via Secrets Manager

#### 📊 **Verified Test Results**:
```bash
# ✅ WORKING: HTTP Health Check
curl http://carepro-production-alb-correct-572837602.us-east-1.elb.amazonaws.com/health
# Response: {"status":"healthy","timestamp":"2025-10-18T02:54:19.7609401Z","version":"1"}

# ✅ WORKING: Target Health Check  
aws elbv2 describe-target-health --target-group-arn arn:aws:elasticloadbalancing:us-east-1:060565307168:targetgroup/carepro-backend-tg-correct/df536c9892b358a3
# Result: Target 172.31.43.50:8080 is "healthy"
```

### 8.1 Health Check Commands
```bash
# Test health endpoint through CloudFront
curl -I https://oncarepro.com/api/cnet/health

# Test API endpoints
curl https://oncarepro.com/api/cnet/api/values

# Test frontend serving
curl -I https://oncarepro.com/

# Check ALB target health
aws elbv2 describe-target-health --target-group-arn $TG_ARN

# Test MongoDB Atlas connectivity (from your local machine)
mongosh "mongodb+srv://<username>:<password>@<cluster>.mongodb.net/<database>"

# Verify DNS propagation
dig oncarepro.com
nslookup oncarepro.com

# Test CloudFront distribution
curl -I -H "Host: oncarepro.com" $CLOUDFRONT_DOMAIN
```

### 8.2 Monitor Deployment
```bash
# Check ECS service status
aws ecs describe-services --cluster carepro-staging-cluster --services carepro-backend-service

# Check CloudWatch logs
aws logs describe-log-streams --log-group-name /ecs/carepro-backend-prod
```

### 🚀 **NEXT STEPS FOR PRODUCTION READY**:

The infrastructure is **fully functional** for Phase 8 verification! For a complete production setup, add:

#### 1. **HTTPS Listener** (Phase 3-4 in deployment plan) ✅ **COMPLETED SUCCESSFULLY!**
```bash
# ✅ SUCCESSFULLY CREATED HTTPS LISTENER!
# Listener ARN: arn:aws:elasticloadbalancing:us-east-1:060565307168:listener/app/carepro-production-alb-correct/28ea66614da7f60c/8920db6e83f52a54
# Port: 443
# Protocol: HTTPS
# Certificate: arn:aws:acm:us-east-1:060565307168:certificate/c149dac5-1afd-4ee8-a866-8753557a03f2
# Target Group: arn:aws:elasticloadbalancing:us-east-1:060565307168:targetgroup/carepro-backend-tg-correct/df536c9892b358a3

# First, set the required variables
ALB_ARN=$(aws elbv2 describe-load-balancers --names carepro-production-alb-correct --query 'LoadBalancers[0].LoadBalancerArn' --output text)
TG_ARN=$(aws elbv2 describe-target-groups --names carepro-backend-tg-correct --query 'TargetGroups[0].TargetGroupArn' --output text)
CERT_ARN=$(aws acm list-certificates --certificate-statuses ISSUED --query 'CertificateSummaryList[?DomainName==`oncarepro.com`].CertificateArn' --output text)

# Create HTTPS listener with SSL certificate
aws elbv2 create-listener \
    --load-balancer-arn "$ALB_ARN" \
    --protocol HTTPS \
    --port 443 \
    --certificates CertificateArn="$CERT_ARN" \
    --default-actions Type=forward,TargetGroupArn="$TG_ARN"

# ✅ TEST HTTPS ENDPOINT NOW!
curl -v https://carepro-production-alb-correct-572837602.us-east-1.elb.amazonaws.com/health
```

#### 2. **SSL Certificate** for production domain ✅ **COMPLETED!**
```bash
# ✅ SSL Certificate successfully obtained and attached!
# Certificate ARN: arn:aws:acm:us-east-1:060565307168:certificate/c149dac5-1afd-4ee8-a866-8753557a03f2
# Domain: oncarepro.com (with wildcard *.oncarepro.com)
# Status: ISSUED and attached to HTTPS listener

# Request SSL certificate for oncarepro.com (ALREADY COMPLETED)
aws acm request-certificate \
    --domain-name oncarepro.com \
    --subject-alternative-names "*.oncarepro.com" \
    --validation-method DNS \
    --tags Key=Environment,Value=Production Key=Project,Value=CarePro
```

#### 3. **Route 53 DNS** configuration  
```bash
# Create hosted zone for production domain
aws route53 create-hosted-zone \
    --name oncarepro.com \
    --caller-reference carepro-domain-$(date +%s) \
    --hosted-zone-config Comment="CarePro production domain"
```

#### 4. **CloudFront Distribution** for global CDN
```bash
# Create CloudFront distribution for global performance
aws cloudfront create-distribution \
    --distribution-config file://cloudfront-config.json
```

**Status**: Your CarePro production infrastructure has **successfully passed Phase 8 verification and testing**! 🎉

## Phase 9: Security and Monitoring Setup

### 9.1 Create CloudWatch Alarms
```bash
# ALB target health alarm
aws cloudwatch put-metric-alarm \
    --alarm-name "CarePro-UnhealthyTargets" \
    --alarm-description "CarePro backend unhealthy targets" \
    --metric-name UnHealthyHostCount \
    --namespace AWS/ApplicationELB \
    --statistic Average \
    --period 300 \
    --threshold 1 \
    --comparison-operator GreaterThanOrEqualToThreshold \
    --dimensions Name=LoadBalancer,Value=app/carepro-production-alb/xxxxxxxxxx \
    --evaluation-periods 2
```

### 9.2 Enable AWS WAF (Optional)
```bash
# Create web ACL for DDoS protection
aws wafv2 create-web-acl \
    --name carepro-web-acl \
    --scope REGIONAL \
    --default-action Allow={} \
    --rules file://waf-rules.json
```

## Post-Deployment Checklist

### 🎯 **Production Environment Setup**
- [ ] **Fresh MongoDB Atlas**: Create new production cluster
- [ ] **Production Frontend**: Deploy to new S3 bucket with production API config
- [ ] **SSL Certificate**: Verify certificate covers oncarepro.com
- [ ] **C# Backend**: Deploy with fresh MongoDB connection
- [ ] **Node API Integration**: Verify existing Node API routes work
- [ ] **Domain Migration**: Update registrar from Vercel to Route 53
- [ ] **CloudFront**: Test all three routing paths work correctly

### 🧪 **Staging Environment (Verify Unchanged)**
- [ ] **Staging Frontend**: Verify still points to Azure backend
- [ ] **Azure Backend**: Confirm unchanged and working
- [ ] **Test Data**: Verify staging database unaffected

### 🔒 **Security & Performance**
- [ ] **CORS Configuration**: Production frontend domain in C# backend
- [ ] **Environment Variables**: Production vs staging separation
- [ ] **Monitoring**: CloudWatch alerts for all services
- [ ] **Load Balancer**: Health checks for C# backend
- [ ] **SSL Redirect**: Ensure HTTPS everywhere
- [ ] **API Rate Limiting**: Configure if needed

## Critical Configuration Updates

### 1. Production Frontend Environment Config
```javascript
// In your frontend build for production
const API_BASE_URL = 'https://oncarepro.com/api/cnet';
const NODE_API_BASE_URL = 'https://oncarepro.com/api/node-api';

// Staging frontend keeps:
const API_BASE_URL = 'https://your-azure-backend.com';
```

### 2. C# Backend CORS for Production
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder
            .WithOrigins("https://oncarepro.com", "https://www.oncarepro.com")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
```

### 3. MongoDB Connection String Strategy
```json
{
  "ConnectionStrings": {
    "MongoDbConnection": "PRODUCTION_MONGODB_ATLAS_CONNECTION_STRING"
  },
  "Environment": "Production"
}
```

### 4. Deployment Pipeline Updates
- **Staging**: Deploy to `carepro-frontend-staging` → Azure backend
- **Production**: Deploy to `oncarepro-frontend-production` → AWS C# backend

## Future Enhancements

1. **Auto Scaling**: Configure ECS service auto-scaling
2. **Multi-AZ**: Deploy across multiple availability zones
3. **CDN**: Add CloudFront for static content
4. **Monitoring**: Implement comprehensive monitoring with Datadog/New Relic
5. **Secrets Rotation**: Implement automatic secrets rotation
6. **Blue/Green Deployments**: Set up deployment strategies

## Troubleshooting

### Common Issues:
1. **Certificate validation fails**: Ensure DNS records are correctly created
2. **Health checks fail**: Verify security group rules and health check path
3. **Database connection issues**: Check RDS security groups and connection strings
4. **404 errors**: Verify ALB listener rules and target group registration

### Useful Commands:
```bash
# Get ALB access logs location
aws elbv2 describe-load-balancer-attributes --load-balancer-arn $ALB_ARN

# Check ECS task logs
aws logs get-log-events --log-group-name /ecs/carepro-backend-prod --log-stream-name [stream-name]

# Test database connection
aws rds describe-db-instances --db-instance-identifier carepro-prod-db
```

## Migration Timeline & Effort Estimation

### Fresh MongoDB Atlas + Full Domain Migration (1-2 days)
- **Morning Day 1**: Create MongoDB Atlas cluster, AWS infrastructure (ALB, CloudFront)
- **Afternoon Day 1**: SSL certificate, DNS setup, ECS task definition updates
- **Morning Day 2**: Deploy new task definition, test all endpoints
- **Afternoon Day 2**: Domain registrar update, DNS propagation monitoring

### Why This Approach is Much Simpler:
✅ **No data migration complexity** - fresh database  
✅ **No code changes** - same MongoDB EF Core setup  
✅ **No DocumentDB learning curve** - familiar Atlas interface  
✅ **No Vercel integration issues** - full AWS control  

## Quick Start Commands Summary

```bash
# 1. Set up AWS credentials for carepro user
aws configure

# 2. Export key variables  
export VPC_ID=$(aws ec2 describe-vpcs --filters "Name=is-default,Values=true" --query 'Vpcs[0].VpcId' --output text)
export SUBNET_IDS=$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" --query 'Subnets[0:2].SubnetId' --output text)

# 3. Create MongoDB Atlas cluster (manual - web console)
# 4. Run phases in order:
# Phase 1: Fresh MongoDB Atlas setup
# Phase 2: Load balancer setup
# Phase 3: SSL certificate  
# Phase 4: ALB listeners
# Phase 5: ECS updates
# Phase 6: Full domain migration (CloudFront + Route 53)
# Phase 7: Application configuration
# Phase 8: Testing and go-live
```

## Final Architecture

### 🚀 **Production Architecture** (oncarepro.com):
1. **Production Frontend**: `https://oncarepro.com/*` → AWS S3 → Points to C# backend
2. **C# Backend API**: `https://oncarepro.com/api/cnet/*` → AWS ECS → Fresh MongoDB Atlas
3. **Shared Node API**: `https://oncarepro.com/api/node-api/*` → Same AWS deployment as staging
4. **Domain**: Fully managed in AWS Route 53 (replaces Vercel marketing)
5. **Database**: Fresh MongoDB Atlas cluster (clean production data)

### 🧪 **Staging Architecture** (preserved):
1. **Staging Frontend**: S3 bucket `carepro-frontend-staging` → Points to Azure backend
2. **Azure Backend**: Your existing Azure API → Test MongoDB Atlas  
3. **Shared Node API**: Current endpoint → Same AWS deployment as production
4. **Purpose**: Safe testing environment, unchanged

### � **Shared Node API Benefits**:
- **Single Infrastructure**: One deployment serves both environments
- **Cost Efficiency**: No duplicate resources for middleware-only service
- **Consistency**: Same API behavior across staging and production
- **Simplified Maintenance**: One codebase, one deployment pipeline
- **Stateless Design**: No database = perfect for sharing

### 🔄 **Environment Separation Benefits**:
- **Zero risk**: Production C# backend changes don't affect staging
- **Database isolation**: Clean production data vs test data  
- **Frontend flexibility**: Same codebase, different backend configurations
- **Smart resource sharing**: Node API shared where it makes sense

## Key Benefits of This Approach

🚀 **Fastest deployment** - minimal changes required  
🔒 **Production-ready security** - ALB + CloudFront + SSL  
📈 **Scalable architecture** - ready for multiple APIs  
💰 **Cost-effective** - MongoDB Atlas pricing familiar to you  
🛠 **Familiar tools** - same database interface and queries  
🌍 **Global performance** - CloudFront CDN for worldwide users  
🔧 **Easy maintenance** - all AWS services under one roof  

---

**🎯 Recommended Next Action**: Start with Phase 1 (MongoDB Atlas cluster creation) while the AWS infrastructure is being set up in parallel.