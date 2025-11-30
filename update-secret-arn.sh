#!/bin/bash
set -e

echo "📥 Getting current task definition..."
TASK_DEF=$(aws ecs describe-task-definition \
  --task-definition carepro-backend-task \
  --region us-east-1 \
  --query 'taskDefinition' \
  --output json)

echo "🔧 Updating Google Sheets secret to use partial ARN..."
NEW_TASK_DEF=$(echo "$TASK_DEF" | jq '
  .containerDefinitions[0].secrets = [
    .containerDefinitions[0].secrets[] | 
    if .name == "GoogleSheets__CredentialsJson" then
      .valueFrom = "carepro/googlesheets/credentials"
    else . end
  ] |
  del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy)
')

echo "📝 Registering new task definition..."
NEW_TASK_ARN=$(aws ecs register-task-definition \
  --cli-input-json "$NEW_TASK_DEF" \
  --region us-east-1 \
  --query 'taskDefinition.taskDefinitionArn' \
  --output text)

echo "✅ New task definition registered: $NEW_TASK_ARN"

echo "🚀 Updating ECS service..."
aws ecs update-service \
  --cluster carepro-cluster \
  --service carepro-backend-service \
  --task-definition "$NEW_TASK_ARN" \
  --region us-east-1 \
  --query 'service.{ServiceName:serviceName,TaskDefinition:taskDefinition}' \
  --output table

echo "✅ Service updated! Deployment in progress..."
echo "📊 Monitor with: aws ecs describe-services --cluster carepro-cluster --services carepro-backend-service --region us-east-1"
