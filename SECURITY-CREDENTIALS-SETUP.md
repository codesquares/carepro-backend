# 🔒 CarePro Security Credentials Setup

## CRITICAL SECURITY NOTICE

**🚨 Your repository had exposed credentials that have been secured. You MUST rotate these credentials immediately:**

### Compromised Credential Types (ROTATE IMMEDIATELY):
1. **MongoDB Connection String**: Database credentials were exposed
2. **JWT Secret Keys**: Authentication tokens compromised  
3. **OpenAI API Key**: AI service credentials exposed
4. **Gmail App Password**: Email service credentials leaked
5. **Flutterwave Keys**: Payment processing credentials exposed
6. **Cloudinary Keys**: File storage API credentials exposed
7. **Google Maps API Key**: Maps service credentials leaked

## Environment Variables Setup

### Required Environment Variables for Production:

```bash
# MongoDB Connection
export ConnectionStrings__MongoDbConnection="mongodb+srv://<NEW_USERNAME>:<NEW_PASSWORD>@<cluster>.mongodb.net/<database>?retryWrites=true&w=majority"

# JWT Configuration  
export JWT__Key="<NEW_SECURE_JWT_SECRET_256_BITS>"
export JwtSettings__Secret="<NEW_SECURE_JWT_SECRET_256_BITS>"

# Email Configuration
export MailSettings__FromEmail="<production_email@domain.com>"
export MailSettings__AppPassword="<NEW_APP_PASSWORD>"

# Payment Processing
export Flutterwave__PublicKey="<NEW_FLUTTERWAVE_PUBLIC_KEY>"
export Flutterwave__SecretKey="<NEW_FLUTTERWAVE_SECRET_KEY>"
export Flutterwave__EncryptionKey="<NEW_FLUTTERWAVE_ENCRYPTION_KEY>"

# Cloud Services
export CloudinarySettings__CloudName="<NEW_CLOUDINARY_CLOUD_NAME>"
export CloudinarySettings__ApiKey="<NEW_CLOUDINARY_API_KEY>"
export CloudinarySettings__ApiSecret="<NEW_CLOUDINARY_API_SECRET>"

# AI Services
export LLMSettings__OpenAI__ApiKey="<NEW_OPENAI_API_KEY>"

# Maps
export GoogleMaps__ApiKey="<NEW_GOOGLE_MAPS_API_KEY>"
```

### AWS Secrets Manager Setup:

```bash
# Create MongoDB secret
aws secretsmanager create-secret \
    --name "carepro/prod/mongodb" \
    --description "CarePro Production MongoDB Connection" \
    --secret-string '{"ConnectionString": "mongodb+srv://<username>:<password>@<cluster>.mongodb.net/<database>"}'

# Create JWT secret  
aws secretsmanager create-secret \
    --name "carepro/prod/jwt" \
    --description "CarePro JWT Secret Key" \
    --secret-string '{"Key": "<256-bit-secret-key>"}'

# Create email secret
aws secretsmanager create-secret \
    --name "carepro/prod/email" \
    --description "CarePro Email Configuration" \
    --secret-string '{"FromEmail": "<email>", "AppPassword": "<password>"}'
```

## IMMEDIATE ACTIONS REQUIRED:

### 1. MongoDB Atlas:
- [ ] Login to MongoDB Atlas
- [ ] Delete user: `codesquareltd` 
- [ ] Create new production user with strong password
- [ ] Update IP whitelist if needed
- [ ] Test new connection string

### 2. OpenAI:
- [ ] Login to OpenAI platform
- [ ] Revoke exposed API key immediately
- [ ] Generate new API key
- [ ] Update usage limits/billing alerts

### 3. Gmail:
- [ ] Login to Gmail account
- [ ] Revoke app password: `kspb tofi rmxk xlte`
- [ ] Generate new app password
- [ ] Update email configuration

### 4. Flutterwave:
- [ ] Login to Flutterwave dashboard
- [ ] Regenerate all API keys (even test keys)
- [ ] Update webhook endpoints if needed

### 5. Cloudinary:
- [ ] Login to Cloudinary
- [ ] Regenerate API secret
- [ ] Review uploaded assets

### 6. Google Maps:
- [ ] Login to Google Cloud Console
- [ ] Check API key usage
- [ ] Regenerate if suspicious activity
- [ ] Set proper restrictions

## Security Best Practices:

1. **Never commit credentials to git**
2. **Use environment variables for all secrets**
3. **Implement proper secret rotation**
4. **Use AWS Secrets Manager in production**
5. **Enable secret scanning in CI/CD**
6. **Regular security audits**

## Testing After Credential Rotation:

```bash
# Test MongoDB connection
dotnet run --environment=Production

# Test API endpoints
curl -X GET https://oncarepro.com/api/health

# Verify email functionality
# Verify payment processing
# Verify file uploads
```

## Monitoring:

- [ ] Set up alerts for failed authentications
- [ ] Monitor API usage patterns
- [ ] Regular credential rotation schedule
- [ ] Security incident response plan

**⚠️ WARNING: Do not deploy until all credentials are rotated and tested!**