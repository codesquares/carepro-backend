# GitHub Actions Workflows Documentation

This repository uses GitHub Actions for continuous integration and deployment. The workflows are designed to ensure code quality, security, and reliable deployments.

## 🔄 Workflow Overview

### 1. **CI Pipeline** (`ci-pipeline.yml`)
**Triggers:** Push to any branch, PRs to main/staging
- ✅ Code quality and security analysis
- ✅ Build and test validation
- ✅ Docker build testing
- ✅ Security vulnerability scanning
- ✅ Dependency checking
- ✅ Environment validation

### 2. **Deploy to Staging** (`deploy-staging.yml`)
**Triggers:** Push to `staging` branch
- 🚀 Automated staging deployment
- 📦 Docker image building and publishing
- 🏥 Health checks and smoke tests
- 📧 Deployment notifications

### 3. **Deploy to Production** (`deploy-production.yml`)
**Triggers:** Push to `main` branch, version tags
- 🔒 Enhanced security validation
- 🏗️ Production-optimized Docker builds
- 📋 Deployment package creation
- ✅ Post-deployment verification

### 4. **Security Scan** (`security-scan.yml`)
**Triggers:** Weekly schedule, security-related changes
- 🔍 Dependency vulnerability scanning
- 🛡️ Static code security analysis
- ⚙️ Configuration security audit
- 🐳 Docker security scanning

### 5. **Code Quality** (`code-quality.yml`)
**Triggers:** PRs, pushes, weekly schedule
- 📊 Static code analysis
- 🏗️ Architecture compliance checking
- 🧪 Test coverage analysis
- ⚡ Performance pattern analysis
- 📚 Documentation quality check

### 6. **PR Validation** (`pr-validation.yml`)
**Triggers:** Pull request events
- ✅ PR title and description validation
- 🌿 Branch naming convention checks
- 🔒 Security checks for PR changes
- 📋 Automated code review checklist
- 👥 Reviewer assignment logic

## 🌳 Branch Strategy

```
main (production)
├── staging (pre-production)
│   ├── feature/smart-contract-generation
│   ├── feature/payment-integration
│   └── bugfix/authentication-fix
└── hotfix/security-patch (emergency fixes)
```

### Branch Protection Rules

#### **Main Branch (Production)**
- ✅ Require pull request reviews (2 approvals)
- ✅ Require status checks to pass
- ✅ Require branches to be up to date
- ✅ Restrict pushes to administrators only
- ✅ Require conversation resolution before merging

#### **Staging Branch**
- ✅ Require pull request reviews (1 approval)
- ✅ Require status checks to pass
- ✅ Allow merge commits disabled (squash/rebase only)

## 🔧 Required Secrets

### Repository Secrets
Configure these in GitHub Settings → Secrets and variables → Actions:

```bash
# Container Registry
GITHUB_TOKEN                    # Auto-provided by GitHub

# Database
MONGODB_CONNECTION              # Production MongoDB connection string

# Authentication
JWT_KEY                         # JWT signing key (min 32 characters)

# Email Service
MAIL_FROM_EMAIL                 # From email address
MAIL_SMTP_SERVER               # SMTP server (smtp.gmail.com)
MAIL_APP_PASSWORD              # App password for email

# Third-party APIs
GOOGLE_MAPS_API_KEY            # Google Maps Geocoding API key
OPENAI_API_KEY                 # OpenAI API key for contract generation

# Cloud Storage
CLOUDINARY_CLOUD_NAME          # Cloudinary cloud name
CLOUDINARY_API_KEY             # Cloudinary API key
CLOUDINARY_API_SECRET          # Cloudinary API secret

# Payment Processing
FLUTTERWAVE_PUBLIC_KEY         # Flutterwave public key
FLUTTERWAVE_SECRET_KEY         # Flutterwave secret key
```

### Environment-Specific Secrets

#### Staging Environment
- All secrets with `_STAGING` suffix for staging-specific values

#### Production Environment
- Use production values for all secrets
- Enable "Required reviewers" for production environment

## 🚀 Deployment Process

### 1. **Feature Development**
```bash
# Create feature branch
git checkout -b feature/new-feature

# Make changes and commit
git add .
git commit -m "feat(feature): add new functionality"

# Push and create PR
git push origin feature/new-feature
# Create PR to staging branch
```

### 2. **Staging Deployment**
```bash
# After PR approval and merge to staging
git checkout staging
git pull origin staging
# Automatic deployment to staging environment
```

### 3. **Production Deployment**
```bash
# After staging validation, create PR from staging to main
git checkout staging
git pull origin staging
# Create PR from staging to main
# After approval and merge, automatic production deployment
```

### 4. **Hotfix Process**
```bash
# For urgent production fixes
git checkout main
git checkout -b hotfix/critical-fix

# Make fix and test
git add .
git commit -m "fix(critical): resolve security issue"

# Create PR directly to main
git push origin hotfix/critical-fix
# Create PR to main (expedited review)
```

## 📊 Monitoring and Alerts

### Health Checks
- **Staging:** `http://staging-api.carepro.com/health`
- **Production:** `http://api.carepro.com/health`

### Key Metrics to Monitor
1. **Build Success Rate:** >95%
2. **Deployment Success Rate:** >98%
3. **Security Scan Pass Rate:** 100%
4. **Test Coverage:** >80% (goal)

### Alert Conditions
- ❌ Build failures on main/staging
- 🔒 Security vulnerabilities detected
- 🚀 Deployment failures
- 📈 Performance degradation

## 🛠️ Local Development Setup

### Prerequisites
```bash
# Install .NET 8 SDK
dotnet --version  # Should be 8.0.x

# Install Docker
docker --version

# Install GitHub CLI (optional)
gh --version
```

### Running Locally
```bash
# Clone repository
git clone https://github.com/codesquares/carepro-backend.git
cd carepro-backend

# Setup user secrets (development only)
dotnet user-secrets init --project CarePro-Api
dotnet user-secrets set "JWT:Key" "your-dev-jwt-key" --project CarePro-Api

# Restore and build
dotnet restore
dotnet build

# Run application
dotnet run --project CarePro-Api
```

### Testing Workflows Locally
```bash
# Install act (GitHub Actions local runner)
# https://github.com/nektos/act

# Test CI pipeline
act push

# Test PR validation
act pull_request
```

## 📋 Checklist for New Features

### Before Creating PR
- [ ] Code follows established patterns
- [ ] Unit tests added/updated
- [ ] Documentation updated
- [ ] No hardcoded secrets
- [ ] Build passes locally
- [ ] Security considerations addressed

### PR Requirements
- [ ] Descriptive title (conventional commits)
- [ ] Detailed description
- [ ] Appropriate reviewers assigned
- [ ] All checks passing
- [ ] No merge conflicts

### After Merge
- [ ] Monitor staging deployment
- [ ] Verify functionality in staging
- [ ] Update documentation if needed
- [ ] Plan production deployment

## 🔍 Troubleshooting

### Common Issues

#### Build Failures
```bash
# Check specific error in Actions tab
# Common fixes:
dotnet clean
dotnet restore
dotnet build
```

#### Security Scan Failures
```bash
# Update vulnerable packages
dotnet list package --vulnerable
dotnet add package <PackageName> --version <LatestVersion>
```

#### Deployment Issues
```bash
# Check environment variables
# Verify Docker image builds locally
docker build -t test-image .
docker run test-image
```

### Getting Help
1. Check workflow logs in GitHub Actions tab
2. Review this documentation
3. Contact the development team
4. Create issue in repository

## 📚 Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Docker Best Practices](https://docs.docker.com/develop/dev-best-practices/)
- [.NET Deployment Guide](https://docs.microsoft.com/en-us/dotnet/core/deploying/)
- [Security Best Practices](https://docs.github.com/en/code-security)