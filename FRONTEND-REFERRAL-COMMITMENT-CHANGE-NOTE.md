# Frontend Handoff: Commitment + Referral + Checkout Updates

Date: 2026-07-11
Audience: Frontend web/mobile teams

This note captures backend contract changes you must implement in frontend now.

## 1) What changed (high impact)

1. Chat unlock is caregiver-wide (not gig-scoped) when commitment gate is enabled.
2. Checkout supports referral code input for recurring plans only.
3. Checkout response now returns `breakdown.referralDiscountApplied`.
4. Referral payout amount is finalized at NGN 5000.
5. Finance/Admin now has referral management + referral redemption exports.

## 2) Behavior changes frontend must reflect

### 2.1 Chat gating behavior

1. Client -> caregiver chat send is blocked unless client has a completed commitment with that caregiver.
2. This check is caregiver-wide (`HasActiveCommitmentWithCaregiverAsync`), so once unlocked for a caregiver, chat is allowed across that caregiver relationship.
3. If commitment gate is paused by backend config, chat should proceed without this check.
4. Caregiver replies are not blocked by this gate.

### 2.2 Checkout + referral behavior

1. `serviceType` accepted values are only: `one-time`, `monthly`.
2. Referral codes are accepted only for recurring (`monthly`) checkout.
3. Referral discount is flat NGN 10000 (if valid), applied at order-fee stage.
4. Referral is one-time per client across platform (after first redemption, client cannot use another code).
5. Self-referral is blocked (email/phone match).
6. If discount would make payable <= 0, checkout is rejected.

### 2.3 Referral payout behavior

1. Backend payout amount for each redemption is now NGN 5000.
2. Payout lifecycle: `Pending` -> `Paid` (Finance action).

### 2.4 How to know commitment is required vs not required

Use this decision flow in frontend:

1. Call `GET /api/booking-commitment/check/{gigId}`.
2. If `commitmentNotRequired === true`, do not show commitment gate/paywall for that gig.
3. Else if `hasAccess === true`, allow checkout/chat UX path without prompting commitment payment.
4. Else show commitment-required UX and route to `POST /api/booking-commitment/initiate` flow.

Important:

1. Global commitment gate on/off is backend-controlled via `CommitmentFeeSettings.Enabled` (appsettings/env), not a frontend toggle.
2. Frontend should not hardcode allow/deny; always rely on backend responses at action time.
3. When backend gate is disabled, chat commitment checks are bypassed server-side.

## 3) Endpoints frontend should use

## 3.1 Chat

### POST /api/Chat/send
Auth: Bearer token required.

Request body:
```json
{
  "receiverId": "string",
  "message": "string"
}
```

Success response (`200`):
```json
{
  "messageId": "string",
  "wasRedacted": true,
  "redactionWarning": "string|null",
  "deliveredMessage": "string|null"
}
```

Failure responses:
2. `400` with `{ "error": "Cannot send messages to yourself" }`
3. `400` with `{ "error": "You must pay the booking commitment fee before messaging this caregiver. Please unlock access from the gig page." }`
4. `400` with contact-policy warning in `error`
5. `400` with `{ "error": "Message exceeds maximum length of 5000 characters" }`
6. `500` with `{ "error": "Failed to send message" }`

SignalR note:
`ChatHub.SendMessage(...)` can throw `HubException` with the same commitment/contact/length messages. Reuse same error mapper in realtime UI.

## 3.2 Booking commitment (unlock flow)

### POST /api/booking-commitment/initiate
Auth: Bearer token required.

Request body:
```json
{
  "gigId": "string",
  "email": "string",
  "redirectUrl": "string"
}
```

Success (`200`): `BookingCommitmentResponse`
```json
{
  "success": true,
  "message": "string",
  "transactionReference": "string",
  "paymentLink": "string|null",
  "amount": 5000,
  "flutterwaveFees": 70,
  "totalCharged": 5070,
  "currency": "NGN"
}
```

Known failures:
1. `401` `{ "success": false, "message": "User not authenticated." }`
2. `400` `{ "success": false, "message": "..." }` (service validation/business rules, comma-joined when multiple)

### GET /api/booking-commitment/check/{gigId}
Auth required.

Response (`200`): `CommitmentStatusResponse`
```json
{
  "hasAccess": true,
  "gigId": "string|null",
  "caregiverId": "string|null",
  "unlockedAt": "2026-07-11T12:00:00Z|null",
  "isAppliedToOrder": false,
  "commitmentNotRequired": false
}
```

UI rule:
If `commitmentNotRequired === true`, hide commitment gate UI on cart/checkout.

### GET /api/booking-commitment/status/{transactionReference}
Auth required.

Response (`200`):
```json
{
  "success": true,
  "status": "completed",
  "transactionReference": "string",
  "flutterwaveTransactionId": "string|null",
  "gigId": "string",
  "caregiverId": "string",
  "amount": 5000,
  "flutterwaveFees": 70,
  "totalCharged": 5070,
  "completedAt": "2026-07-11T12:00:00Z|null",
  "isAppliedToOrder": false,
  "errorMessage": "string|null"
}
```

Known failures:
1. `404` `{ "success": false, "message": "Commitment not found." }`
2. `403` for non-owner access.

## 3.3 Checkout payments (main change)

### POST /api/payments/initiate
Auth required.

Request body (new `referralCode`):
```json
{
  "gigId": "string",
  "serviceType": "one-time|monthly",
  "frequencyPerWeek": 1,
  "email": "string",
  "redirectUrl": "https://...",
  "referralCode": "CAREPRO-REF-20260711-ABC123"
}
```

Success (`200`): `PendingPaymentResponse`
```json
{
  "success": true,
  "message": "string",
  "transactionReference": "string",
  "paymentLink": "string|null",
  "breakdown": {
    "basePrice": 0,
    "serviceType": "monthly",
    "frequencyPerWeek": 3,
    "orderFee": 0,
    "serviceCharge": 0,
    "flutterwaveFees": 0,
    "totalAmount": 0,
    "currency": "NGN",
    "commitmentFeeDeducted": 5000,
    "referralDiscountApplied": 10000
  }
}
```

Key frontend actions:
1. Add referral code input on checkout page.
2. Show referral discount line item from `breakdown.referralDiscountApplied`.
3. Keep commitment deduction line item from `breakdown.commitmentFeeDeducted`.
4. Treat `serviceType` as strict enum (`one-time`, `monthly`).

Known failures (`400`, `{ success:false, message:"..." }`):
1. `ServiceType must be 'one-time' or 'monthly'.`
2. `You must pay the booking commitment fee before purchasing this gig. Please unlock access from the gig page first.`
3. `Referral codes can only be used on recurring services.`
4. `Invalid or inactive referral code.`
5. `Self-referral is not allowed.`
6. `This client has already used a referral and cannot use another code.`
7. `This order is not eligible for referral discount because the payable amount would be zero or negative.`
8. `This order cannot be processed because the payable amount after discount is zero or negative.`
9. `You already have an active order for this gig. You cannot pay for the same gig twice while an order is still in progress.`
10. `You have already purchased this gig on a recurring plan. You cannot purchase it again.`
11. `Failed to initialize payment with Flutterwave.`

### GET /api/payments/status/{transactionReference}
Auth required.

Response (`200`): `PaymentStatusResponse`
```json
{
  "success": true,
  "status": "completed|pending|failed|expired|amountmismatch",
  "transactionReference": "string",
  "flutterwaveTransactionId": "string|null",
  "paymentDate": "2026-07-11T12:00:00Z|null",
  "clientOrderId": "string|null",
  "breakdown": {
    "basePrice": 0,
    "serviceType": "monthly",
    "frequencyPerWeek": 3,
    "orderFee": 0,
    "serviceCharge": 0,
    "flutterwaveFees": 0,
    "totalAmount": 0,
    "currency": "NGN",
    "commitmentFeeDeducted": 5000,
    "referralDiscountApplied": 10000
  },
  "errorMessage": "string|null"
}
```

Failures:
1. `404` `{ "success": false, "message": "Payment not found." }`
2. `403` for non-owner access.

## 3.4 Referral management (Admin UI)

All below require `ReferralManagementPolicy` (Finance Admin or SuperAdmin).

### POST /api/referrals/referrers
Create a referrer (+ optional bank account).

Request:
```json
{
  "fullName": "John Doe",
  "email": "john@example.com",
  "phoneNo": "+234...",
  "bankAccount": {
    "fullName": "John Doe",
    "bankName": "GTBank",
    "accountNumber": "0123456789",
    "accountName": "JOHN DOE"
  }
}
```

Response (`200`):
```json
{
  "success": true,
  "data": {
    "id": "...",
    "fullName": "John Doe",
    "email": "john@example.com",
    "phoneNo": "+234...",
    "createdAt": "2026-07-11T12:00:00Z"
  }
}
```

Failure (`400`): `{ "success": false, "message": "..." }`
Examples: `FullName is required.`, `Email is required.`, `A referrer with this email already exists.`

### GET /api/referrals/referrers
Get all referrers (with IDs) for admin selection.

Response (`200`):
```json
{
  "success": true,
  "data": [
    {
      "id": "688f9d8b6aa8c8a2d97d5e30",
      "fullName": "John Doe",
      "email": "john@example.com",
      "phoneNo": "+234...",
      "createdAt": "2026-07-11T12:00:00Z"
    }
  ]
}
```

How frontend retrieves the ID for code generation:
1. Call `GET /api/referrals/referrers` when opening the create-code form.
2. Display each record as `fullName + email` for admin selection.
3. Send selected `data[].id` as `referrerId` to `POST /api/referrals/codes`.
4. If a referrer was just created, you can also use `data.id` from `POST /api/referrals/referrers` immediately.
5. Fallback route if your gateway caches method rules: `GET /api/referrals/referrers/list`.

### POST /api/referrals/codes
Generate a code for referrer.

Request:
```json
{
  "referrerId": "string"
}
```

Response (`200`):
```json
{
  "success": true,
  "data": {
    "id": "...",
    "code": "CAREPRO-REF-20260711-ABC123",
    "referrerId": "...",
    "createdAt": "2026-07-11T12:00:00Z",
    "isActive": true
  }
}
```

Failure examples:
1. `Invalid ReferrerId format.`
2. `Referrer not found.`

### GET /api/referrals/redemptions?startDate=...&endDate=...
List referral redemptions.

Response (`200`):
```json
{
  "success": true,
  "data": [
    {
      "redemptionId": "...",
      "referralCode": "CAREPRO-REF-20260711-ABC123",
      "referrerName": "John Doe",
      "referrerEmail": "john@example.com",
      "referrerPhoneNo": "+234...",
      "clientId": "...",
      "orderId": "...",
      "discountAmount": 10000,
      "payoutStatus": "Pending|Paid",
      "payoutAmount": 5000,
      "redeemedAt": "2026-07-11T12:00:00Z",
      "paidAt": "2026-07-31T12:00:00Z|null"
    }
  ]
}
```

### POST /api/referrals/redemptions/{redemptionId}/mark-paid
Response (`200`):
```json
{ "success": true, "message": "Redemption marked as paid." }
```

Failure examples:
1. `Invalid redemption ID format.`
2. `Redemption not found.`

## 3.5 Admin export additions

### GET /api/admin/export/referral-redemptions?startDate=...&endDate=...
Policy: `AnalyticsPolicy`.

Response: XLSX file download.

Export columns:
1. Redemption ID
2. Redeemed At
3. Referral Code
4. Referrer Name
5. Referrer Email
6. Referrer Phone
7. Client ID
8. Order ID
9. Discount Amount
10. Payout Amount
11. Payout Status
12. Paid At

## 4) Error code mapping for frontend

Backend currently returns human-readable messages (no stable machine error code field yet).
Implement FE-side mapping to a local `uiErrorCode` by message/status.

Suggested mapping:

1. `COMMITMENT_REQUIRED_FOR_CHAT`
Message contains: `You must pay the booking commitment fee before messaging this caregiver`

2. `COMMITMENT_REQUIRED_FOR_CHECKOUT`
Message contains: `You must pay the booking commitment fee before purchasing this gig`

3. `REFERRAL_RECURRING_ONLY`
Message equals: `Referral codes can only be used on recurring services.`

4. `REFERRAL_INVALID_OR_INACTIVE`
Message equals: `Invalid or inactive referral code.`

5. `REFERRAL_SELF_NOT_ALLOWED`
Message equals: `Self-referral is not allowed.`

6. `REFERRAL_ALREADY_USED_BY_CLIENT`
Message equals: `This client has already used a referral and cannot use another code.`

7. `REFERRAL_PAYABLE_NON_POSITIVE`
Message contains: `payable amount` and `zero or negative`

8. `CHECKOUT_DUPLICATE_ACTIVE_ORDER`
Message starts with: `You already have an active order for this gig.`

9. `CHECKOUT_REPURCHASE_RECURRING_BLOCKED`
Message starts with: `You have already purchased this gig on a recurring plan.`

10. `CHAT_CONTACT_POLICY_BLOCK`
HTTP 400 from chat send where message is contact-policy warning.

11. `VALIDATION_ERROR`
Generic fallback for `400` with validation text.

12. `AUTH_REQUIRED`
HTTP `401`.

13. `FORBIDDEN`
HTTP `403`.

14. `NOT_FOUND`
HTTP `404`.

15. `SERVER_ERROR`
HTTP `500`.

## 5) Implementation checklist for frontend

1. Add referral code input on checkout form.
2. Send `referralCode` in `/api/payments/initiate` request body.
3. Render `breakdown.referralDiscountApplied` in payment summary.
4. Keep `breakdown.commitmentFeeDeducted` rendering.
5. Update `serviceType` UI enum to exactly `one-time | monthly`.
6. In chat UX, handle commitment-required errors and deep-link to commitment unlock flow.
7. In finance admin UI, add referrer/code/redemption screens using `/api/referrals/*`.
8. In analytics/admin UI, add export action for `/api/admin/export/referral-redemptions`.
9. Centralize error-message mapping using section 4 above.

## 6) Product notes for frontend copy

1. Referral discount shown to client at checkout: NGN 10000 (eligible recurring only).
2. Referrer payout is internal finance settlement amount: NGN 5000 per qualified redemption.
3. Chat unlock copy should mention caregiver unlock, not only gig unlock.
4. Keep messages from backend as source of truth in user-visible toasts where possible.
