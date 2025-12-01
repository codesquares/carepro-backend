#!/bin/bash

# Get the current task definition
TASK_DEF=$(aws ecs describe-task-definition --task-definition carepro-backend-task)

# Extract the task definition JSON and add the new environment variable
NEW_TASK_DEF=$(echo "$TASK_DEF" | jq '.taskDefinition | 
  .containerDefinitions[0].environment += [{"name": "JwtSettings__Secret", "value": "FbyNPnypbq9sHRASSNa36FLojgXh/APDCU6gGym7888="}] |
  {
    family: .family,
    taskRoleArn: .taskRoleArn,
    executionRoleArn: .executionRoleArn,
    networkMode: .networkMode,
    containerDefinitions: .containerDefinitions,
    requiresCompatibilities: .requiresCompatibilities,
    cpu: .cpu,
    memory: .memory
  }')

# Register the new task definition
echo "Registering new task definition..."
NEW_TASK_DEF_ARN=$(echo "$NEW_TASK_DEF" | aws ecs register-task-definition --cli-input-json file:///dev/stdin | jq -r '.taskDefinition.taskDefinitionArn')

echo "New task definition registered: $NEW_TASK_DEF_ARN"

# Update the service to use the new task definition
echo "Updating service to use new task definition..."
aws ecs update-service \
  --cluster carepro-cluster \
  --service carepro-backend-service \
  --task-definition carepro-backend-task \
  --force-new-deployment

echo "Service update initiated. Waiting for deployment to complete..."
aws ecs wait services-stable --cluster carepro-cluster --services carepro-backend-service

echo "Deployment completed successfully!"
