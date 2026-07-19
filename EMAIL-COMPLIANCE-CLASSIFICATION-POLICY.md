# Email Compliance Classification Policy

Last updated: 2026-07-19
Owner: Backend Platform Team
Scope: CarePro backend email system

## Purpose
This document is the source of truth for classifying every currently catalogued email type as either:
- Always-Send (Transactional/Operational/Compliance)
- Preference-Gated (Lifecycle/Engagement/Marketing)

No new email type may ship without an explicit entry in this policy.

## Enforcement Rule
1. Every new email type must be added to this policy during implementation.
2. PRs introducing new email sends must include:
- policy entry
- classification rationale
- preference-gating behavior confirmation
- unsubscribe header behavior confirmation for preference-gated types
3. Unknown/unlisted types are release blockers.

## Classification Matrix (27 Catalogued Types)

| # | Catalogued Email Type | Current Sender Surface | Classification | Rationale |
|---|---|---|---|---|
| 1 | Signup verification | SendSignUpVerificationEmailAsync | Always-Send | Account access and security-critical |
| 2 | Caregiver welcome | SendCaregiverWelcomeEmailAsync | Always-Send | Onboarding completion after explicit account action |
| 3 | Password reset | SendPasswordResetEmailAsync | Always-Send | Security/account recovery |
| 4 | Unread notification reminder | SendNotificationEmailAsync | Always-Send | Product operational notice tied to pending user actions |
| 5 | New gig opportunity | SendNewGigNotificationEmailAsync | Preference-Gated | Discovery/engagement type |
| 6 | System notification | SendSystemNotificationEmailAsync | Always-Send | Admin/system operational updates |
| 7 | Withdrawal status update | SendWithdrawalStatusEmailAsync | Always-Send | Financial transaction lifecycle |
| 8 | Withdrawal request submitted | SendWithdrawalRequestEmailAsync | Always-Send | Financial transaction lifecycle |
| 9 | Payment confirmation | SendPaymentConfirmationEmailAsync | Always-Send | Financial receipt/confirmation |
| 10 | Earnings added | SendEarningsNotificationEmailAsync | Always-Send | Financial settlement visibility |
| 11 | Order received | SendOrderReceivedEmailAsync | Always-Send | Live order workflow state |
| 12 | Order confirmation | SendOrderConfirmationEmailAsync | Always-Send | Live order workflow state |
| 13 | Order completed (client) | SendOrderCompletedEmailAsync | Always-Send | Live order closure |
| 14 | Order completed (caregiver) | SendOrderCompletedCaregiverEmailAsync | Always-Send | Live order closure and earnings release |
| 15 | Order cancelled | SendOrderCancelledEmailAsync | Always-Send | Live order exception state |
| 16 | Payment failed | SendPaymentFailedEmailAsync | Always-Send | Financial action required |
| 17 | Payment action required | SendPaymentActionRequiredEmailAsync | Always-Send | Financial action required |
| 18 | Refund processed/approved | SendRefundNotificationEmailAsync | Always-Send | Financial refund lifecycle |
| 19 | Generic notification wrapper (business-typed usage) | SendGenericNotificationEmailAsync | Mixed by scenario | Wrapper is not a business type by itself. Use explicit scenario mapping below. |
| 20 | Daily unread chat digest | SendBatchMessageNotificationEmailAsync | Preference-Gated | Engagement retention communication |
| 21 | Contract reminder cadence | SendContractReminderEmailAsync | Preference-Gated | Follow-up/reminder lifecycle cadence |
| 22 | Contract PDF delivery | SendContractPdfEmailAsync | Always-Send | Formal service document delivery |
| 23 | Payment receipt PDF delivery | SendPaymentReceiptEmailAsync | Always-Send | Financial receipt/document |
| 24 | Account deletion scheduled | SendAccountDeletionScheduledEmailAsync | Always-Send | Compliance and legal data rights process |
| 25 | Account deletion cancelled | SendAccountDeletionCancelledEmailAsync | Always-Send | Compliance and legal data rights process |
| 26 | Final deletion completion notice | Generic template in hard-delete flow | Always-Send | Compliance and legal data rights process |
| 27 | Admin one-to-one / bulk outreach | SendCustomEmailToUserAsync | Preference-Gated | Outbound campaign/admin outreach category |

## Explicit Decision on Previously Questioned Types
The following are classified as Always-Send (moved out of preference-gated set):
- care_request_not_selected
- care_request_closed
- care_request_paused

Reason:
These are operational status outcomes for processes the caregiver actively participated in. Suppressing them risks user confusion and missed workflow outcomes.

## Generic Wrapper Scenario Mapping (for #19)
When using SendGenericNotificationEmailAsync, implementers must explicitly select one branch:
- Always-Send branch: order/payment/refund/dispute/compliance/active workflow state changes.
- Preference-Gated branch: discovery, engagement, reminder cadence, marketplace opportunity broadcasts.

Code requirement:
- preferenceGated must be set to true only for the preference-gated branch.
- preferenceGated must remain false for always-send scenarios.

## One-Click Unsubscribe Requirement (Preference-Gated Only)
Every preference-gated email must include both headers:
- List-Unsubscribe
- List-Unsubscribe-Post: List-Unsubscribe=One-Click

And include a body unsubscribe link to the same tokenized endpoint.

## Future Change Procedure
For each newly added email type:
1. Add entry to this policy with classification and rationale.
2. Add/adjust enforcement logic in EmailNotificationTrackingService.
3. Add tests for:
- preference behavior
- header presence for preference-gated sends
4. Include evidence output in CI logs.
