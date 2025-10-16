# CarePro Backend - Security & Configuration Guide

## 🔒 Security Notice

This repository includes comprehensive security measures to prevent accidental exposure of sensitive information.

## 📁 Files Excluded from Git (Sensitive Data)

The following files are **automatically ignored** by git and should **never be committed**:

### Configuration Files
- `appsettings*.json` - Contains API keys, database connections, and secrets
- `.env` - Environment variables with credentials
- `CarePro-Api/appsettings.json` - Main configuration file with all secrets

### AWS Files
- `iam-policy-*.json` - IAM policies with account-specific information
- `ecs-*.json` - ECS configurations with account details
- `aws-secrets-setup.sh` - Script with AWS secrets
- `setup-iam-permissions.sh` - IAM setup script
- `get-backend-url.sh` - Backend URL retrieval script
- `get-test-ip.sh` - IP retrieval script
- `*-policy.json` - All policy files
- `*-trust-policy.json` - Trust policy files

### Build Artifacts
- `bin/` directories - Compiled binaries
- `obj/` directories - Build objects
- `*.dll`, `*.pdb` files - Compiled assemblies

### Deployment Files
- `docker-deploy.sh` - May contain sensitive deployment info
- `*-deployment.json` - Deployment configurations
- `*-service-definition.json` - Service definitions

### SSL/Security Files
- `*.pfx`, `*.p12` - SSL certificates
- `*.key`, `*.pem`, `*.crt` - Security keys and certificates

### Documentation with Sensitive Info
- `DEPLOYMENT-GUIDE.md` - Contains deployment secrets
- `IAM-SETUP-GUIDE.md` - Contains AWS account information
- `GET-PUBLIC-IP-INSTRUCTIONS.md` - Contains infrastructure details
- `QUICK-IAM-SETUP.md` - Contains setup secrets

## ✅ Safe Configuration Template

Use `CarePro-Api/appsettings.template.json` as a template for your local configuration:

1. Copy `appsettings.template.json` to `appsettings.json`
2. Replace all placeholder values with your actual credentials
3. Never commit the `appsettings.json` file

## 🔧 Local Development Setup

1. **Environment Setup:**
   ```bash
   cp .env.example .env
   # Edit .env with your local configuration
   ```

2. **Configuration Setup:**
   ```bash
   cp CarePro-Api/appsettings.template.json CarePro-Api/appsettings.json
   # Edit appsettings.json with your credentials
   ```

3. **Build Project:**
   ```bash
   dotnet build
   ```

## 🚫 What NOT to Commit

❌ **Never commit these files:**
- `appsettings.json` (contains real API keys)
- `.env` (contains environment secrets)
- Any file with real API keys, passwords, or tokens
- AWS account-specific configurations
- SSL certificates or private keys

✅ **Safe to commit:**
- `appsettings.template.json` (placeholder values only)
- `.env.example` (example environment variables)
- Source code files
- Documentation without sensitive information

## 🔍 Checking for Sensitive Data

Before committing, always check:

```bash
# Check what files are being committed
git status

# Check for sensitive patterns in staged files
git diff --cached | grep -i "password\|secret\|key\|token"

# View ignored files
git status --ignored
```

## 🛡️ Security Best Practices

1. **Never hardcode secrets** in source code
2. **Use environment variables** for configuration
3. **Review commits** before pushing
4. **Use the template files** provided
5. **Keep sensitive files local** only

## 📞 Support

If you accidentally commit sensitive information:
1. **DO NOT** push to remote repository
2. Remove the sensitive data immediately
3. Use `git reset` or `git commit --amend` to fix the commit
4. Rotate any exposed credentials

---

**Remember: When in doubt, don't commit it!** 🔒