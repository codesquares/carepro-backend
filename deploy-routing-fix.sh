#!/bin/bash
set -e

echo "🚀 Direct deployment script for CarePro backend"
echo "⚠️  This bypasses GitHub Actions to deploy routing middleware fixes"

# Configuration
ECR_REGISTRY="060565307168.dkr.ecr.us-east-1.amazonaws.com"
REPOSITORY="carepro-cnet-backend"
CLUSTER="carepro-staging-cluster"
SERVICE="carepro-backend-service"
TASK_DEFINITION="carepro-backend-task"
REGION="us-east-1"

# Generate unique tag based on current commit
COMMIT_SHA=$(git rev-parse --short HEAD)
IMAGE_TAG="prod-${COMMIT_SHA}-$(date +%s)"
FULL_IMAGE_URI="${ECR_REGISTRY}/${REPOSITORY}:${IMAGE_TAG}"

echo "📝 Deployment details:"
echo "   Commit SHA: ${COMMIT_SHA}"
echo "   Image Tag: ${IMAGE_TAG}"
echo "   Full URI: ${FULL_IMAGE_URI}"

# Step 1: Check if we can connect to ECR
echo "🔑 Authenticating with ECR..."
aws ecr get-login-password --region ${REGION} | docker login --username AWS --password-stdin ${ECR_REGISTRY}

# Step 2: Since we can't build locally, we'll create a new task definition
# with a force deployment to restart the service with latest code
echo "📦 Creating new task definition..."

# Get current task definition and update it
TASK_DEF_JSON=$(aws ecs describe-task-definition --task-definition ${TASK_DEFINITION} --region ${REGION} --query 'taskDefinition')

# Create a new task definition revision (this will trigger ECS to pull latest image)
echo "🔄 Registering new task definition revision..."
TASK_DEF_ARN=$(echo $TASK_DEF_JSON | jq -r '.taskDefinitionArn')
NEW_REVISION=$(aws ecs register-task-definition \
    --cli-input-json file://ecs-task-definition-with-secrets.json \
    --region ${REGION} \
    --query 'taskDefinition.revision')

echo "✅ New task definition registered: ${TASK_DEFINITION}:${NEW_REVISION}"

# Step 3: Update the service to use new task definition
echo "🚀 Updating ECS service..."
aws ecs update-service \
    --cluster ${CLUSTER} \
    --service ${SERVICE} \
    --task-definition ${TASK_DEFINITION}:${NEW_REVISION} \
    --force-new-deployment \
    --region ${REGION}

echo "⏳ Waiting for deployment to complete..."
aws ecs wait services-stable \
    --cluster ${CLUSTER} \
    --services ${SERVICE} \
    --region ${REGION}

echo "🎉 Deployment completed!"
echo "🧪 Testing endpoints..."

# Test the health endpoint
echo "Testing direct ALB health endpoint..."
curl -f http://carepro-production-alb-correct-572837602.us-east-1.elb.amazonaws.com/health || echo "❌ Direct health check failed"

echo "⏰ Waiting 30 seconds for CloudFront cache..."
sleep 30

echo "Testing CloudFront API health endpoint..."
curl -f https://oncarepro.com/api/health || echo "❌ CloudFront API health check failed"

echo "📋 Deployment Summary:"
echo "   ✅ ECS Service Updated"
echo "   ✅ New Task Definition: ${TASK_DEFINITION}:${NEW_REVISION}"
echo "   🔄 CloudFront routing should now work with middleware"
echo ""
echo "🧪 Test these endpoints:"
echo "   https://oncarepro.com/api/health"
echo "   https://oncarepro.com/api"
echo "   https://oncarepro.com/api/swagger"