# Certificate Validation System - Frontend Integration Notice

## Overview
We've implemented a comprehensive certificate validation system that goes beyond basic document verification to detect forged, fake, or mismatched certificates. This ensures only genuine Nigerian educational certificates are accepted.

## What We've Implemented

### 1. Certificate Type Whitelist
Only 4 approved Nigerian educational certificates are accepted:
- West African Senior School Certificate Examination (WASSCE)
- National Examination Council (NECO) Senior School Certificate Examination (SSCE)
- National Business and Technical Examinations Board (NABTEB)
- National Youth Service Corps (NYSC) Certificate

Each must have the correct issuer (WAEC, NECO, NABTEB, NYSC respectively).

### 2. Multi-Layer Genuineness Validation
After Dojah verification, we perform 5 additional checks:

**a) Confidence Threshold**
- < 50% confidence → **Rejected** (Invalid)
- 50-70% confidence → **Manual Review Required**
- ≥ 70% confidence → **Verified** (Auto-approved)

**b) Name Matching**
- Extracted certificate names must match caregiver profile (fuzzy matching for typos)
- Complete mismatches → **Manual Review Required**

**c) Country Validation**
- Document must be from Nigeria (NG/NGA)
- Non-Nigerian certificates → **Rejected**

**d) Document Type Cross-Validation**
- Dojah detected document type must match claimed certificate
- Prevents uploading wrong document (e.g., Driver's License as WASSCE) → **Rejected**

**e) Issue Date Validation**
- Must be in the past and after 1960
- Future dates or unreasonable dates → **Rejected**

### 3. Enhanced Notifications
Caregivers receive email + in-app notifications with:
- **Verified**: Congratulations message
- **Invalid/Rejected**: Specific reasons for rejection
- **Manual Review**: What's being reviewed and expected timeframe
- **Verification Failed**: Technical error, can retry

## API Response Changes

### Certificate Upload Response (`POST /api/Certificates/upload`)

```json
{
  "certificateId": "673f2a1b8c9d4e1234567890",
  "uploadStatus": "success",
  "certificateUrl": "https://res.cloudinary.com/...",
  "verification": {
    "status": "Verified" | "Invalid" | "ManualReviewRequired" | "VerificationFailed",
    "confidence": 0.85,
    "verifiedAt": "2025-11-26T10:30:00Z",
    "errorMessage": "Certificate verification confidence meets threshold for auto-approval.",
    "extractedInfo": {
      "firstName": "John",
      "lastName": "Doe",
      "documentNumber": "ABC123456",
      "issueDate": "2018-06-15T00:00:00Z"
    }
  }
}
```

### Key Changes to Handle:

1. **`errorMessage` Field**
   - Now contains detailed validation reasons (pipe-separated for multiple issues)
   - Examples:
     - `"Certificate verification confidence (45%) is too low. Minimum acceptable confidence is 50%."`
     - `"Name mismatch: Certificate shows 'Jane Smith' but profile shows 'John Doe'. Manual review required. | Invalid certificate country: Expected Nigeria (NG) but detected 'GH'."`

2. **New Status: `ManualReviewRequired`**
   - Display differently from `Invalid`
   - Show message: "Your certificate is under review by our team"
   - Estimated review time: 24-48 hours

3. **`confidence` Score**
   - Display as percentage (multiply by 100)
   - Show confidence bar for transparency

## UI/UX Recommendations

### Certificate Upload Flow
```
1. User uploads certificate
   ↓
2. Show loading: "Verifying your certificate..."
   ↓
3. Display result based on status:
   
   ✅ Verified (confidence ≥ 70%)
      → Success message
      → Show confidence score
      → Explain next steps
   
   ⚠️ Manual Review Required (confidence 50-70% OR name mismatch)
      → Warning message
      → Show specific reason from errorMessage
      → Explain review process (24-48 hours)
      → Disable re-upload until reviewed
   
   ❌ Invalid/Rejected (confidence < 50% OR failed validation)
      → Error message
      → Show specific reasons from errorMessage
      → Allow immediate re-upload
      → Provide guidance on common issues
   
   🔄 Verification Failed (technical error)
      → System error message
      → Show "Retry Verification" button
      → Contact support if persists
```

### Error Message Display
Parse `errorMessage` by splitting on `" | "` to show multiple validation failures:

```javascript
const errors = response.verification.errorMessage.split(' | ');
errors.forEach(error => {
  // Display each error as a list item
});
```

### Status Badge Colors
- **Verified**: Green (#22c55e)
- **ManualReviewRequired**: Yellow/Orange (#f59e0b)
- **Invalid**: Red (#ef4444)
- **VerificationFailed**: Gray (#6b7280)
- **PendingVerification**: Blue (#3b82f6)

## Certificate List View Updates

When displaying caregiver certificates:

```json
{
  "certificateId": "673f2a1b8c9d4e1234567890",
  "certificateName": "West African Senior School Certificate Examination (WASSCE)",
  "certificateIssuer": "West African Examinations Council (WAEC)",
  "isVerified": true,
  "verificationStatus": "Verified",
  "verificationConfidence": 0.85,
  "verificationDate": "2025-11-26T10:30:00Z",
  "certificateUrl": "https://res.cloudinary.com/..."
}
```

Add visual indicators:
- Confidence percentage badge
- Status badge with appropriate color
- Last verified date
- "Retry Verification" button for failed verifications

## Admin Dashboard Requirements

Admins need interface to:
1. View all certificates with status `ManualReviewRequired`
2. See extracted info vs profile info side-by-side
3. View Dojah's raw response and confidence scores
4. Manually approve/reject with notes
5. Update certificate status from backend

## Validation Error Messages

Common rejection reasons caregivers will see:

| Scenario | Error Message |
|----------|---------------|
| Low confidence | "Certificate verification confidence (45%) is too low. Minimum acceptable confidence is 50%." |
| Name mismatch | "Name mismatch: Certificate shows 'Jane Smith' but profile shows 'John Doe'. Manual review required." |
| Wrong country | "Invalid certificate country: Expected Nigeria (NG) but detected 'GH'. Only Nigerian educational certificates are accepted." |
| Wrong document type | "Document type mismatch: You claimed 'WASSCE' but Dojah detected 'Driver License'. This may indicate a forged certificate." |
| Future issue date | "Certificate issue date cannot be in the future." |
| Invalid certificate type | "Invalid certificate type. Only WASSCE, NECO SSCE, NABTEB, and NYSC certificates are accepted." |
| Wrong issuer | "Invalid certificate issuer. Expected issuer for this certificate type is: West African Examinations Council (WAEC)" |
| Duplicate certificate | "A certificate of type 'WASSCE' has already been uploaded for this caregiver." |

## Testing Checklist

- [ ] Display all 6 verification statuses correctly
- [ ] Parse and show multiple error messages from `errorMessage` field
- [ ] Handle confidence scores (display as percentage)
- [ ] Show appropriate icons/badges for each status
- [ ] Implement retry verification for failed verifications
- [ ] Display extracted certificate information
- [ ] Show verification date/timestamp
- [ ] Handle admin manual review workflow
- [ ] Test with various error scenarios
- [ ] Mobile responsive for status badges and messages

## Support Contact
For questions about this integration, contact the backend team or refer to:
- Certificate validation code: `Infrastructure/Content/Helpers/CertificateValidationHelper.cs`
- Certificate service: `Infrastructure/Content/Services/CertificationService.cs`
- Validation documentation: This file

---

**Implementation Date**: November 26, 2025  
**Deployed To**: Production (ECS Task Definition revision 26+)  
**Breaking Changes**: None - only enhanced validation and new `ManualReviewRequired` status
