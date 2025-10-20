#!/bin/bash

# Script to give carepro user administrator access
# Run this with an existing AWS administrator account

set -e

echo "🔐 Making carepro user an administrator..."

# Check current user
CURRENT_USER=$(aws sts get-caller-identity --query 'Arn' --output text)
echo "👤 Current user: $CURRENT_USER"

# Verify carepro user exists
echo "🔍 Checking if carepro user exists..."
aws iam get-user --user-name carepro || {
    echo "❌ Error: carepro user does not exist"
    echo "Please create the carepro user first"
    exit 1
}

echo "🚀 Attaching AdministratorAccess policy to carepro user..."

# Attach administrator access
aws iam attach-user-policy \
    --user-name carepro \
    --policy-arn "arn:aws:iam::aws:policy/AdministratorAccess"

echo "✅ Administrator access granted!"

# Verify the policy is attached
echo "🔍 Verifying policy attachment..."
aws iam list-attached-user-policies --user-name carepro

echo ""
echo "🎉 SUCCESS: carepro user now has administrator access!"
echo ""
echo "⚠️  SECURITY NOTICE:"
echo "   The carepro user now has FULL access to ALL AWS services"
echo "   This includes billing, IAM, and all resources"
echo "   Use this power responsibly!"
echo ""
echo "📋 Next steps:"
echo "1. Switch to carepro user credentials"
echo "2. Run: aws sts get-caller-identity (should show carepro user)"
echo "3. Test: aws s3 ls (should work without errors)"
echo "4. Proceed with deployment discovery and setup"
echo ""
echo "🔒 For production, consider using least-privilege policies later"