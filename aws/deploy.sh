#!/bin/bash

# AWS Deployment Script for CarePro Backend
# This script helps deploy the CarePro backend to AWS ECS using Fargate

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
AWS_REGION="${AWS_REGION:-us-east-1}"
ECR_REPOSITORY="carepro-cnet-backend"
ECS_CLUSTER="carepro-cluster"
ECS_SERVICE="carepro-backend-service"
TASK_DEFINITION_FAMILY="carepro-backend"

print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_aws_cli() {
    if ! command -v aws &> /dev/null; then
        print_error "AWS CLI is not installed"
        exit 1
    fi
    
    if ! aws sts get-caller-identity &> /dev/null; then
        print_error "AWS CLI is not configured or credentials are invalid"
        exit 1
    fi
    
    print_success "AWS CLI is configured"
}

get_account_id() {
    aws sts get-caller-identity --query Account --output text
}

create_ecr_repository() {
    print_info "Creating ECR repository if it doesn't exist..."
    
    aws ecr describe-repositories --repository-names ${ECR_REPOSITORY} --region ${AWS_REGION} &> /dev/null || \
    aws ecr create-repository \
        --repository-name ${ECR_REPOSITORY} \
        --region ${AWS_REGION} \
        --image-scanning-configuration scanOnPush=true
    
    print_success "ECR repository ready"
}

login_to_ecr() {
    print_info "Logging in to ECR..."
    
    aws ecr get-login-password --region ${AWS_REGION} | \
    docker login --username AWS --password-stdin $(get_account_id).dkr.ecr.${AWS_REGION}.amazonaws.com
    
    print_success "Logged in to ECR"
}

build_and_push_image() {
    local account_id=$(get_account_id)
    local ecr_uri="${account_id}.dkr.ecr.${AWS_REGION}.amazonaws.com/${ECR_REPOSITORY}"
    
    print_info "Building Docker image..."
    docker build -t ${ECR_REPOSITORY}:latest .
    
    print_info "Tagging image for ECR..."
    docker tag ${ECR_REPOSITORY}:latest ${ecr_uri}:latest
    docker tag ${ECR_REPOSITORY}:latest ${ecr_uri}:$(git rev-parse --short HEAD)
    
    print_info "Pushing image to ECR..."
    docker push ${ecr_uri}:latest
    docker push ${ecr_uri}:$(git rev-parse --short HEAD)
    
    print_success "Image pushed to ECR"
    echo "Image URI: ${ecr_uri}:latest"
}

create_ecs_cluster() {
    print_info "Creating ECS cluster if it doesn't exist..."
    
    if ! aws ecs describe-clusters --clusters ${ECS_CLUSTER} --region ${AWS_REGION} &> /dev/null; then
        aws ecs create-cluster \
            --cluster-name ${ECS_CLUSTER} \
            --capacity-providers FARGATE \
            --default-capacity-provider-strategy capacityProvider=FARGATE,weight=1 \
            --region ${AWS_REGION}
        print_success "ECS cluster created"
    else
        print_info "ECS cluster already exists"
    fi
}

update_task_definition() {
    local account_id=$(get_account_id)
    local image_uri="${account_id}.dkr.ecr.${AWS_REGION}.amazonaws.com/${ECR_REPOSITORY}:latest"
    
    print_info "Updating task definition..."
    
    # Update the task definition JSON with actual account ID and image URI
    sed "s/YOUR_ACCOUNT_ID/${account_id}/g" aws/ecs-task-definition.json | \
    sed "s|YOUR_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/carepro-backend:latest|${image_uri}|g" > /tmp/ecs-task-definition.json
    
    aws ecs register-task-definition \
        --cli-input-json file:///tmp/ecs-task-definition.json \
        --region ${AWS_REGION}
    
    print_success "Task definition updated"
}

create_or_update_service() {
    print_info "Creating or updating ECS service..."
    
    if aws ecs describe-services --cluster ${ECS_CLUSTER} --services ${ECS_SERVICE} --region ${AWS_REGION} | grep -q "ACTIVE"; then
        print_info "Updating existing service..."
        aws ecs update-service \
            --cluster ${ECS_CLUSTER} \
            --service ${ECS_SERVICE} \
            --task-definition ${TASK_DEFINITION_FAMILY} \
            --region ${AWS_REGION}
    else
        print_info "Creating new service..."
        aws ecs create-service \
            --cluster ${ECS_CLUSTER} \
            --service-name ${ECS_SERVICE} \
            --task-definition ${TASK_DEFINITION_FAMILY} \
            --desired-count 2 \
            --launch-type FARGATE \
            --network-configuration "awsvpcConfiguration={subnets=[subnet-12345,subnet-67890],securityGroups=[sg-12345],assignPublicIp=ENABLED}" \
            --region ${AWS_REGION}
    fi
    
    print_success "Service updated"
}

wait_for_deployment() {
    print_info "Waiting for deployment to complete..."
    
    aws ecs wait services-stable \
        --cluster ${ECS_CLUSTER} \
        --services ${ECS_SERVICE} \
        --region ${AWS_REGION}
    
    print_success "Deployment completed successfully"
}

show_service_info() {
    print_info "Service information:"
    
    aws ecs describe-services \
        --cluster ${ECS_CLUSTER} \
        --services ${ECS_SERVICE} \
        --region ${AWS_REGION} \
        --query 'services[0].{Status:status,RunningCount:runningCount,PendingCount:pendingCount,TaskDefinition:taskDefinition}' \
        --output table
}

deploy_full() {
    print_info "Starting full deployment to AWS..."
    
    check_aws_cli
    create_ecr_repository
    login_to_ecr
    build_and_push_image
    create_ecs_cluster
    update_task_definition
    create_or_update_service
    wait_for_deployment
    show_service_info
    
    print_success "Deployment completed successfully!"
}

show_help() {
    cat << EOF
AWS Deployment Script for CarePro Backend

Usage: $0 [COMMAND]

Commands:
    deploy      Full deployment pipeline
    build       Build and push Docker image only
    update      Update ECS service with latest task definition
    status      Show current service status
    logs        Show service logs
    rollback    Rollback to previous task definition
    cleanup     Clean up unused resources
    help        Show this help message

Environment Variables:
    AWS_REGION              AWS region (default: us-east-1)
    AWS_ACCESS_KEY_ID       AWS access key
    AWS_SECRET_ACCESS_KEY   AWS secret key

Prerequisites:
    - AWS CLI installed and configured
    - Docker installed
    - Proper IAM permissions for ECS, ECR, and related services
    - VPC with subnets and security groups configured

EOF
}

case "${1:-help}" in
    deploy)
        deploy_full
        ;;
    build)
        check_aws_cli
        create_ecr_repository
        login_to_ecr
        build_and_push_image
        ;;
    update)
        check_aws_cli
        update_task_definition
        create_or_update_service
        wait_for_deployment
        ;;
    status)
        check_aws_cli
        show_service_info
        ;;
    logs)
        print_info "Showing recent logs..."
        aws logs tail /ecs/carepro-backend --follow --region ${AWS_REGION}
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Unknown command: $1"
        show_help
        exit 1
        ;;
esac