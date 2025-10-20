#!/bin/bash

# CarePro IAM Permissions Setup Script
# This script adds all necessary permissions to the carepro user for managing deployments

set -e

echo "🔐 Setting up comprehensive IAM permissions for carepro user..."

# Check if we're using the correct user
CURRENT_USER=$(aws sts get-caller-identity --query 'Arn' --output text)
if [[ $CURRENT_USER != *"user/carepro"* ]]; then
    echo "❌ Error: You must be logged in as the 'carepro' user"
    echo "Current user: $CURRENT_USER"
    exit 1
fi

ACCOUNT_ID=$(aws sts get-caller-identity --query 'Account' --output text)
POLICY_NAME="CareProFullDeploymentPolicy"

echo "📋 Account ID: $ACCOUNT_ID"
echo "👤 Policy Name: $POLICY_NAME"

# Create the comprehensive IAM policy
echo "📝 Creating IAM policy..."
aws iam create-policy \
    --policy-name $POLICY_NAME \
    --policy-document file://iam-policy-carepro-full-deployment.json \
    --description "Comprehensive policy for CarePro deployments (C# backend, Node API, Frontend)" || {
        echo "ℹ️  Policy might already exist, attempting to create new version..."
        
        # If policy exists, create a new version
        aws iam create-policy-version \
            --policy-arn "arn:aws:iam::$ACCOUNT_ID:policy/$POLICY_NAME" \
            --policy-document file://iam-policy-carepro-full-deployment.json \
            --set-as-default
    }

# Attach the policy to the carepro user
echo "🔗 Attaching policy to carepro user..."
aws iam attach-user-policy \
    --user-name carepro \
    --policy-arn "arn:aws:iam::$ACCOUNT_ID:policy/$POLICY_NAME"

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
echo "   ✅ Lambda - for serverless functions (if needed)"
echo "   ✅ API Gateway - for API management"
echo "   ✅ DocumentDB/RDS - for databases"
echo ""
echo "🚀 Ready to manage all three deployments!"

# Test a few key permissions
echo "🧪 Testing key permissions..."

echo "   - ECS clusters: $(aws ecs list-clusters --query 'clusterArns | length(@)') found"
echo "   - S3 buckets: $(aws s3 ls 2>/dev/null | wc -l || echo "Testing...") accessible"
echo "   - Route 53 zones: $(aws route53 list-hosted-zones --query 'HostedZones | length(@)') found"

echo ""
echo "✨ All permissions configured successfully!"