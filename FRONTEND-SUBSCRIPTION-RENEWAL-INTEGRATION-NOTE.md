# Frontend Integration Note: Client-Initiated Subscription Renewal

Date: 2026-06-25
Audience: Frontend team
Status: Ready for integration

---

## 1) What changed

We added a client-initiated renewal path so users can recover failed or auth-required recurring payments instead of waiting only for background auto-retry.

This is additive and does not remove auto-renew.

New capabilities:
- Client can manually trigger renewal for eligible subscriptions.
- Frontend can query renewal status for polling and state recovery.
- Recurring failed webhook events are now mapped to subscription payment history and retry policy.

---

## 2) New endpoints

Base URL: /api/subscriptions
Auth: Required (Bearer token)

### 2.1 POST /api/subscriptions/{subscriptionId}/renew

Purpose:
- Trigger a manual renewal attempt for a subscription.

Request body:
```json
{
  "redirectUrl": "https://oncarepro.com/subscription/payment-confirmed"
}
```

Field rules:
- redirectUrl: optional string
- If omitted, backend uses configured frontend URL fallback.

Success response (200):
```json
{
  "success": true,
  "data": {
    "success": true,
    "outcome": "success",
    "message": "Renewal payment processed successfully.",
    "subscriptionId": "686f2...",
    "nextAction": "refresh_subscription",
    "latestPaymentAttempt": {
      "id": "6870a...",
      "transactionReference": "CAREPRO-RECURRING-20260625-AB12CD34",
      "flutterwaveTransactionId": "2059001122",
      "amount": 83655,
      "currency": "NGN",
      "status": "successful",
      "errorMessage": null,
      "authorizationUrl": null,
      "initiatedBy": "client",
      "billingCycleNumber": 3,
      "attemptedAt": "2026-06-25T10:10:08Z",
      "completedAt": "2026-06-25T10:10:21Z",
      "clientOrderId": "686f8..."
    }
  }
}
```

Action-required response (200):
```json
{
  "success": true,
  "data": {
    "success": false,
    "outcome": "action_required",
    "message": "Awaiting cardholder authentication.",
    "subscriptionId": "686f2...",
    "nextAction": "complete_authorization",
    "latestPaymentAttempt": {
      "id": "6870b...",
      "transactionReference": "CAREPRO-RECURRING-20260625-EF56GH78",
      "flutterwaveTransactionId": null,
      "amount": 83655,
      "currency": "NGN",
      "status": "pending",
      "errorMessage": "Awaiting cardholder authentication",
      "authorizationUrl": "https://checkout.flutterwave.com/v3/hosted/pay/...",
      "initiatedBy": "client",
      "billingCycleNumber": 3,
      "attemptedAt": "2026-06-25T10:16:02Z",
      "completedAt": null,
      "clientOrderId": null
    }
  }
}
```

Failed response (400):
```json
{
  "success": false,
  "message": "Cannot renew subscription while status is 'Paused'.",
  "data": {
    "success": false,
    "outcome": "failed",
    "message": "Cannot renew subscription while status is 'Paused'.",
    "subscriptionId": "686f2...",
    "nextAction": "retry_or_update_payment_method",
    "latestPaymentAttempt": null
  }
}
```

Common error responses:
- 401: user not authenticated
- 400: not authorized for this subscription, invalid subscription state, charge already in progress, no payment token, provider-side charge failures

---

### 2.2 GET /api/subscriptions/{subscriptionId}/renew/status

Purpose:
- Return latest renewal state, next action, and latest payment attempt details.
- Use for polling after redirect and for page reload recovery.

Success response (200):
```json
{
  "success": true,
  "data": {
    "subscriptionId": "686f2...",
    "subscriptionStatus": "PastDue",
    "renewalState": "action_required",
    "nextAction": "complete_authorization",
    "failedChargeAttempts": 1,
    "nextChargeDate": "2026-06-25T12:20:00Z",
    "latestPaymentAttempt": {
      "id": "6870b...",
      "transactionReference": "CAREPRO-RECURRING-20260625-EF56GH78",
      "flutterwaveTransactionId": null,
      "amount": 83655,
      "currency": "NGN",
      "status": "pending",
      "errorMessage": "Awaiting cardholder authentication",
      "authorizationUrl": "https://checkout.flutterwave.com/v3/hosted/pay/...",
      "initiatedBy": "client",
      "billingCycleNumber": 3,
      "attemptedAt": "2026-06-25T10:16:02Z",
      "completedAt": null,
      "clientOrderId": null
    }
  }
}
```

Common error responses:
- 401: user not authenticated
- 400: subscription not found, not authorized

---

## 3) Updated field contract

SubscriptionPaymentRecordDTO now includes:
- authorizationUrl: string nullable
- initiatedBy: string ("system" or "client")

This applies anywhere payment history is returned, including:
- GET /api/subscriptions/{subscriptionId}/payments
- renew and renew/status payloads

---

## 4) Renewal state machine for frontend

renewalState values from renew/status:
- none
- paid
- action_required
- failed

nextAction values:
- none
- refresh_subscription
- complete_authorization
- retry_now
- update_payment_method
- wait

Recommended frontend behavior:
1. If renewalState is action_required and latestPaymentAttempt.authorizationUrl exists:
- Show Continue payment CTA.
- CTA opens latestPaymentAttempt.authorizationUrl.

2. If renewalState is failed and nextAction is retry_now:
- Show Retry payment CTA (calls POST renew).

3. If nextAction is update_payment_method:
- Send user to existing payment method update flow:
- POST /api/subscriptions/{subscriptionId}/payment-method

4. After payment provider redirect back:
- Call GET renew/status immediately.
- Poll every 5-10 seconds for up to 2-3 minutes.
- Stop when renewalState becomes paid or failed.

---

## 5) UX rules

- Always disable Renew button while request is in flight.
- If renew returns action_required, switch button label to Continue payment.
- If authorizationUrl is missing but state is action_required, show fallback message and keep polling status.
- Show failedChargeAttempts and clear guidance:
  - Retry now
  - Update payment method
- Do not assume query parameters on callback are sufficient.
- Source of truth is GET renew/status.

---

## 6) Error handling checklist

For POST renew:
- 401: redirect to login
- 400 with message containing "Not authorized": show forbidden state
- 400 with message containing "status is": display status-specific guidance
- 400 with provider failure message: show retry and update-card options

For GET renew/status:
- 401: redirect to login
- 400 not found/not authorized: show generic subscription unavailable

Network failures:
- Keep last known UI state and show non-blocking retry toast.

---

## 7) Security and consistency notes

- Amount is never accepted from frontend.
- Backend computes and validates charge amount.
- Webhook verification remains server-side.
- Failed recurring webhooks now update payment history/retry tracking.

---

## 8) Quick integration sequence

1. User opens subscription details.
2. Call GET /api/subscriptions/{id}/renew/status.
3. Render state-driven CTA.
4. On Retry payment CTA click, call POST /api/subscriptions/{id}/renew.
5. If outcome=action_required and authorizationUrl exists, redirect/open auth URL.
6. On return, poll GET renew/status until terminal state.
7. On paid, refresh subscription summary and payment history.

---

## 9) Backward compatibility

- Existing subscription endpoints are unchanged.
- Auto-renew background job remains active.
- New endpoints are additive.
