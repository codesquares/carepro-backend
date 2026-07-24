# Email Compliance Classification Policy

Last updated: 2026-07-23
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

## Classification Matrix (28 Catalogued Types)

| # | Catalogued Email Type | Current Sender Surface | Classification | Rationale |
|---|---|---|---|---|
| 1 | Signup verification | SendSignUpVerificationEmailAsync | Always-Send | Account access and security-critical |
| 2 | Caregiver welcome | SendCaregiverWelcomeEmailAsync | Always-Send | Onboarding completion after explicit account action |
| 3 | Password reset | SendPasswordResetEmailAsync | Always-Send | Security/account recovery |
| 4 | Unread notification reminder | SendNotificationEmailAsync | Always-Send | Product operational notice tied to pending user actions |
| 5 | New gig opportunity | SendNewGigNotificationEmailAsync | Preference-Gated | Business-critical (a caregiver's actual income opportunities) — gated on `EmailNotifications` alone, deliberately independent of marketing consent (`MarketingEmails`). See Gap Closure below. |
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
| 28 | Gig draft saved | SendDraftGeneratedEmailAsync (NotificationTypes.DraftGenerated / "draft_generated") | Preference-Gated | Self-initiated workflow confirmation, not a live/active transaction or compliance record. Same family as GigPublished/GigPaused/GigDeleted; closest existing analogue is #5 (New gig opportunity). |
| 29 | Care request match/response updates | Direct calls in `CareRequestMatchingService` (`SendMatchNotificationEmailToCaregiverAsync`) and `CareRequestResponseService` (`SendClientResponderEmailAsync`, inline hire email) — CareRequestNewMatch, CareRequestNewResponder, CareRequestHired | Preference-Gated | Business-critical (a caregiver's actual work opportunities and hiring outcomes) — gated via `IEmailService.SendGenericNotificationEmailAsync`'s `gateUserId`/`gateNotificationType` params, which call `ShouldSendEmailToUserAsync`. For caregiver recipients this evaluates `EmailNotifications && CareRequestUpdates`; the responder-notification recipient is the client, gated on the client's general marketing consent instead (`CareRequestUpdates` is a caregiver-only preference field). See Correction below. |

## Correction — 2026-07-23: Row #29 mechanism and coverage were both wrong

A discovery pass found row #29's stated mechanism ("ImmediateNotificationProcessor") did not match the code. Confirmed against the actual implementation:

- `ImmediateNotificationProcessor` never processes any CareRequest* type — its query filter (`_immediateOnceOnlyTypes` in `EmailNotificationTrackingService`) never included them, so the preference check in `ShouldSendEmailToUserAsync` was never reached via that path for these types.
- Three of the six types instead sent email through a separate, hardcoded direct-call path (`SendMatchNotificationEmailToCaregiverAsync`, `SendClientResponderEmailAsync`, and an inline call in `HireResponderAsync`), all via `IEmailService.SendGenericNotificationEmailAsync(..., preferenceGated: true)`. That parameter only added `List-Unsubscribe` headers — it never checked `ShouldSendEmailToUserAsync` or any stored preference. So `CareRequestNewMatch`, `CareRequestNewResponder`, and `CareRequestHired` emails were sent unconditionally to every recipient regardless of their `CareRequestUpdates` setting.
- `CareRequestShortlisted` and `CareRequestReopened` had (and still have) no email path at all — only an in-app notification.
- `CareRequestMatched` was a dead constant, never dispatched anywhere; it has been removed from `NotificationTypes` and both gating hash sets.

Fixed in this pass: `preferenceGated` was renamed to `includeUnsubscribeHeader` (an honest name for what it actually does), and two new optional parameters (`gateUserId`, `gateNotificationType`) were added to `SendGenericNotificationEmailAsync` — when supplied, the method now calls `ShouldSendEmailToUserAsync` before sending and skips the send if the recipient's preferences block it. The three active call sites for `CareRequestNewMatch`, `CareRequestNewResponder`, and `CareRequestHired` now pass these parameters and are genuinely preference-gated. All other existing callers of `SendGenericNotificationEmailAsync` were left behaviorally unchanged (rename only).

Not addressed in this pass (tracked separately): whether to migrate these three direct calls into the standard `ImmediateNotificationProcessor` path, and whether `CareRequestShortlisted`/`CareRequestReopened`/`CareRequestNotSelected`/`CareRequestClosed`/`CareRequestPaused` should get email paths built at all.

## Gap Closure — 2026-07-23
`DraftGenerated` (#28) predates this policy document (introduced 2026-05-26, ~8 weeks before this document's creation on 2026-07-19) but was never backfilled into the matrix. It was already correctly wired into `_preferenceGatedTypes` / `HasGeneralMarketingConsent` in code — this entry formalizes that existing classification, it does not change behavior. Verified directly against a live instance with real caregiver preference records:
- Caregiver with `emailNotifications: false` → draft-save produced zero `EmailNotificationLogs` entries; processor logged "Skipping email ... due to preferences".
- Caregiver with `emailNotifications: true, marketingEmails: true` → draft-save produced one `EmailNotificationLogs` entry (`Status: Sent`) and a real SMTP delivery.

Note: the same audit surfaced 16 other preference-gated types (GigPublished, GigPaused, GigDeleted, the CareRequest* family, the Contract* family, PriceNegotiationExpired) that are also correctly gated in code but similarly absent from this matrix. Those are intentionally out of scope for this entry and tracked separately, not resolved here.

## Reclassification — 2026-07-23: NewGig and CareRequestUpdates decoupled from marketing consent

Before this change, `EmailNotificationTrackingService.ShouldSendEmailToUserAsync` gated **every** caregiver preference-gated notification type — including #5 (New gig opportunity) and #29 (care request updates) — behind `HasGeneralMarketingConsent` (`EmailNotifications && (MarketingEmails || Promotions)`) as a blanket prerequisite, before any per-type check ran. A caregiver who wanted gig/care-request notifications but had declined marketing received neither, with no indication in the UI that the two were linked.

These two types were reclassified: `_caregiverNewGigTypes` and `_caregiverCareRequestTypes` are now checked *before* the marketing-consent gate and evaluate only `EmailNotifications && <type flag>`. They remain Preference-Gated overall (a caregiver can still turn them off individually, or via `EmailNotifications`), they are simply no longer coupled to the separate marketing-consent toggle. All other caregiver preference-gated types (GigPublished, GigPaused, GigDeleted, DraftGenerated, ChatMessage, the Contract* family, PriceNegotiationExpired) still fall through to the original `HasGeneralMarketingConsent` gate — this reclassification is intentionally scoped to the two business-critical types identified, not a redesign of the whole gate.

Also collapsed in this pass: `MarketingEmails` and `Promotions` were functionally identical on the caregiver preference model (same OR formula, no email type distinguished between them). `Promotions` has been removed from `CaregiverNotificationPreferences`, its DTOs, and the caregiver settings UI; `MarketingEmails` is now the sole marketing-consent toggle for caregivers. (The client-side `NotificationPreferences` model has the identical redundancy and was left untouched — out of scope for this caregiver-settings pass.)

Verified with a real test against a live database: a caregiver preference record with `EmailNotifications: true, MarketingEmails: false, NewGig: true, CareRequestUpdates: true` now evaluates both `NewGig` and `CareRequestNewMatch` notification checks as **allowed** (previously both were blocked by the marketing-consent prerequisite). `EmailNotifications: false` still blocks both regardless of the individual toggles.

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
