#!/bin/bash

# Script for AWS Administrator to set up CarePro user permissions
# This script should be run by an AWS administrator, NOT the carepro user

set -e

echo "🔐 AWS Administrator: Setting up CarePro user permissions..."

# Check if we're NOT using the carepro user (admin should run this)
CURRENT_USER=$(aws sts get-caller-identity --query 'Arn' --output text)
if [[ $CURRENT_USER == *"user/carepro"* ]]; then
    echo "❌ Error: This script should be run by an AWS ADMINISTRATOR, not the carepro user"
    echo "Please run this script with administrator credentials"
    exit 1
fi

ACCOUNT_ID=$(aws sts get-caller-identity --query 'Account' --output text)
POLICY_NAME="CareProFullDeploymentPolicy"

echo "📋 Account ID: $ACCOUNT_ID"
echo "👤 Admin User: $CURRENT_USER"
echo "🎯 Target User: carepro"
echo "📝 Policy Name: $POLICY_NAME"

# Check if carepro user exists
echo "🔍 Checking if carepro user exists..."
aws iam get-user --user-name carepro || {
    echo "❌ Error: carepro user does not exist"
    echo "Please create the carepro user first"
    exit 1
}

# Create the comprehensive IAM policy
echo "📝 Creating IAM policy..."
POLICY_ARN="arn:aws:iam::$ACCOUNT_ID:policy/$POLICY_NAME"

aws iam create-policy \
    --policy-name $POLICY_NAME \
    --policy-document file://iam-policy-carepro-full-deployment.json \
    --description "Comprehensive policy for CarePro deployments (C# backend, Node API, Frontend)" || {
        echo "ℹ️  Policy already exists, creating new version..."
        
        # Delete old versions if at limit
        VERSIONS=$(aws iam list-policy-versions --policy-arn $POLICY_ARN --query 'Versions[?!IsDefaultVersion].[VersionId]' --output text)
        for version in $VERSIONS; do
            if [ ! -z "$version" ]; then
                echo "🗑️  Deleting old policy version: $version"
                aws iam delete-policy-version --policy-arn $POLICY_ARN --version-id $version || true
            fi
        done
        
        # Create new version
        aws iam create-policy-version \
            --policy-arn $POLICY_ARN \
            --policy-document file://iam-policy-carepro-full-deployment.json \
            --set-as-default
    }

# Attach the policy to the carepro user
echo "🔗 Attaching policy to carepro user..."
aws iam attach-user-policy \
    --user-name carepro \
    --policy-arn $POLICY_ARN

echo "✅ Policy attached successfully!"

# Verify the policy is attached
echo "🔍 Verifying policy attachment..."
aws iam list-attached-user-policies --user-name carepro

echo ""
echo "🎉 IAM permissions setup complete!"
echo ""
echo "📊 The carepro user now has permissions for:"
echo "   ✅ ECS (Elastic Container Service) - for backend and Node API"
echo "   ✅ ECR (Elastic Container Registry) - for Docker images"
echo "   ✅ EC2 (VPC, Security Groups, Subnets) - for networking"
echo "   ✅ ALB (Application Load Balancer) - for load balancing"
echo "   ✅ Route 53 - for DNS management"
echo "   ✅ CloudFront - for CDN and distribution"
echo "   ✅ S3 - for frontend static hosting"
echo "   ✅ ACM (Certificate Manager) - for SSL certificates"
echo "   ✅ Secrets Manager - for secure credential storage"
echo "   ✅ CloudWatch - for monitoring and logging"
echo "   ✅ Lambda - for serverless functions"
echo "   ✅ API Gateway - for API management"
echo ""
echo "🚀 The carepro user is now ready to manage all three deployments!"
echo ""
echo "📋 Next steps:"
echo "1. Have the carepro user test with: aws s3 ls"
echo "2. Have the carepro user run deployment discovery commands"
echo "3. Proceed with the unified deployment plan"