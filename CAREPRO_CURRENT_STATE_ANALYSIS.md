# CarePro Marketplace --- Current State Analysis & Gap Assessment

**Date:** February 26, 2026  
**Scope:** Analysis of the existing backend implementation against the architectural areas identified in the Care Marketplace Design Review document.  
**Context:** Nigeria-focused care marketplace platform.

---

## Executive Summary

The CarePro backend is a **.NET/C# API** backed by **MongoDB**, using **Flutterwave** for payments and **Dojah** for identity verification. The platform currently supports caregiver onboarding, gig creation, client ordering, payment processing, contract generation, real-time chat (SignalR), and a financial ledger system. The design review document identified 11 architectural areas; this analysis maps what already exists in the codebase against each, highlights gaps, and contextualises risks for the Nigerian market.

---

## 1. Marketplace Flow (Current Implementation)

### What Exists

| Step | Implementation Status | Evidence |
|---|---|---|
| Caregiver onboards | **Implemented** | `CareGiversController`, `AppUser` entity, role assignment |
| Identity verification | **Implemented** | `VerificationService`, `DojahDocumentVerificationService`, Dojah webhook integration |
| Assessment system | **Implemented** | `AssessmentsController`, `AssessmentSession` entity, `QuestionBank`, `EligibilityService` |
| Gig creation | **Implemented** | `GigsController`, `GigServices`, eligibility-gated publishing |
| Client onboards | **Implemented** | `ClientsController`, `Client` entity |
| Client selects gig & pays | **Implemented** | `PaymentsController`, `PendingPaymentService`, Flutterwave integration |
| Negotiation & contract | **Implemented** | `ContractController` (caregiver-initiated generation, client approve/review/reject) |
| Contract approval flow | **Partially Implemented** | 2-round negotiation exists; no escrow conversion trigger on approval |
| Refund/rematch on rejection | **Partially Implemented** | Dispute mechanism exists (`HasDispute`, `DisputeReason`), wallet debit for refunds; automated rematch is not implemented |

### How It Works Today

1. Client selects a gig and initiates payment via `/api/payments/initiate`.
2. Server calculates pricing (base price + 10% service charge + Flutterwave gateway fees), creates a `PendingPayment`, and redirects to Flutterwave checkout.
3. Flutterwave webhook confirms payment; `CompletePaymentAsync` creates a `ClientOrder` and a `BillingRecord`.
4. Caregiver generates a contract via `/api/contracts/caregiver/generate` with schedule details.
5. Client can approve, request review (Round 1), or reject (Round 2 only).
6. On approval the contract status changes; service nominally begins.

### Gap

The design review recommends **payment before negotiation** (Booking Commitment Fee) rather than payment before contract. Currently, the full service amount is collected before contract generation, which is the exact "Payment Before Defined Scope" vulnerability identified in Section 2.1 of the review.

---

## 2. Structural Vulnerabilities --- Current Exposure

### 2.1 Payment Before Defined Scope

**Current state:** Full payment is collected via Flutterwave before any contract or negotiation takes place. The `PendingPaymentService.CreatePendingPaymentAsync` method prices the gig, charges the client, and only after webhook confirmation does the `ClientOrder` get created. Contract generation is an entirely separate, later step.

**Nigeria-specific risk:** Nigerian diaspora clients (paying in foreign currency or via international cards) face real FX exposure if a refund is needed. Flutterwave charges are non-recoverable on refund (1.4% of amount, capped at NGN 2,000). With the trust deficit common in Nigerian digital services, collecting the full amount upfront amplifies chargeback risk.

**What is missing:**
- No "Booking Commitment Fee" (partial, non-refundable unlock payment) layer
- No separation between "engagement payment" and "service escrow payment"
- No staged payment collection tied to contract milestones

---

### 2.2 Caregiver-Authored Contracts

**Current state:** The system has moved away from pure free-form caregiver contracts. Contracts are generated based on structured data: the caregiver submits a `CaregiverContractGenerationDTO` with schedule, service address, and notes. The `ContractService` then uses a `ContractGenerationDataDTO` enriched with real party information and package details to produce contract terms (the `GeneratedTerms` field, populated via an LLM-based generation pipeline).

**What is working well:**
- Structured schedule input (day of week, start/end time, 4-6 hour validation)
- Package and pricing data comes from the existing order, not from caregiver input
- Negotiation history is tracked via `ContractNegotiationHistory` entity
- 2-round limit prevents infinite negotiation loops

**What is missing:**
- No pre-approved template library with mandatory clauses (liability, escalation, cancellation)
- LLM-generated terms introduce variability; no clause-locking mechanism to ensure critical legal text remains intact
- No explicit medical disclaimer or scope limitation clause enforcement
- In the Nigerian context, contracts should reference the Nigerian Labour Act (for contractor classification) and potentially the National Health Act 2014 (for care standards)

---

### 2.3 Off-Platform Leakage (Disintermediation)

**Current state:** The platform has a `ChatHub` (SignalR) for real-time messaging with a `ContentSanitizer` that strips XSS-type attacks (script tags, data URIs, event handlers). Messages are stored in `ChatMessage` entities via `ChatRepository`.

**What exists:**
- XSS/injection prevention via `ContentSanitizer`
- Authenticated chat (JWT-based user identity enforced)
- Anti-spoofing (sender ID overridden from JWT, not user input)
- User connection status tracking

**What is missing:**
- **No phone number or email detection/blocking in chat messages** --- the design review calls for "Phone/email auto-blocking in chat" but the `ContentSanitizer` only handles HTML/script injection, not contact information patterns
- **No external link filtering** --- users can freely share links, including WhatsApp numbers, social media handles, and external contact methods
- **No keyword detection** for common Nigerian communication platform references (WhatsApp, Telegram, "call me on", "my number is", etc.)
- **No non-circumvention clause enforcement in the messaging layer** --- while this is a legal/contractual matter, the platform has no technical guardrails
- Communication logging exists (messages are stored), but there is no flagging or alert system for suspicious patterns

**Nigeria-specific concern:** WhatsApp is the dominant communication tool in Nigeria. Users will instinctively try to move conversations to WhatsApp. Without active detection and blocking of phone number patterns (Nigerian format: 080x, 081x, 090x, 070x, +234), the platform is highly vulnerable to bypass.

---

### 2.4 Unlimited Negotiation Loop

**Current state:** **Largely addressed.** The contract system enforces a maximum of 2 negotiation rounds:
- Round 1: Caregiver generates, client can approve or request review
- Round 2: Caregiver revises, client can approve or reject (triggers new caregiver request)

The `IContractService` interface enforces this via separate methods: `ClientRequestReviewAsync` (Round 1 only) and `ClientRejectContractAsync` (Round 2 only).

**Remaining gap:**
- There is no cost to the client for entering negotiation. The design review recommends a "Booking Commitment Fee" that makes negotiation non-trivial for the client.
- No timeout on negotiation rounds. A client or caregiver can leave the negotiation indefinitely without penalty.
- No caregiver notification escalation when negotiations stall.

---

### 2.5 Refund Abuse

**Current state:** The system has several refund-related mechanisms:
- `CaregiverWalletService.DebitRefundAsync` --- debits from caregiver withdrawable/pending balance
- `EarningsLedgerService.RecordRefundAsync` --- creates a negative ledger entry
- `SubscriptionService` supports pro-rated refunds on cancellation
- `BillingRecordService` can mark records as refunded

**What is missing:**
- **No refund eligibility flag** --- the design review calls for a per-payment `refundEligibilityFlag` that tracks whether a payment qualifies for refund
- **No non-refundable payment category** --- all payments appear to be technically refundable; there is no "engagement fee" that is excluded from refund
- **No refund reason classification** --- refunds are processed but not categorised (client-initiated, dispute, service failure, etc.)
- **No refund limit tracking** --- no mechanism to detect and block serial refund abusers
- **No Flutterwave fee absorption accounting** --- when a refund is issued, the platform absorbs the 1.4% gateway fee, but this cost is not tracked or reported

**Nigeria-specific risk:** Given the lower average transaction values and Flutterwave's fee structure, refund abuse disproportionately erodes margins. A NGN 50,000 service with a NGN 700 gateway fee results in a net loss of NGN 700 per speculative booking that is refunded.

---

### 2.6 Legal Classification Risk

**Current state:** The platform uses language consistent with a marketplace/facilitator model:
- Caregivers are treated as independent entities with their own gigs
- The `CareGiver` and `Client` entities suggest a two-sided marketplace
- Caregivers manage their own bank accounts (`CaregiverBankAccount` entity)
- Withdrawal is caregiver-initiated (`WithdrawalRequest` entity)
- The platform charges a 10% service commission (defined as `SERVICE_CHARGE_RATE = 0.10m`)

**What is missing:**
- **No explicit contractor/independent professional classification clause in system-generated contracts** --- the review recommends this be embedded in every contract
- **No platform terms of service enforcement layer** --- while the backend exists, there's no acceptance tracking for updated T&Cs
- **No evidence of legal review alignment with Nigerian law** --- the Federal Competition and Consumer Protection Act (FCCPA) 2018 and the NITDA Data Protection Regulation (NDPR) 2019 impose obligations on digital marketplaces operating in Nigeria

---

## 3. Recommended Model Implementation --- Current Progress

### Stage 1: Paid Unlock (Commitment Layer)

| Requirement | Status |
|---|---|
| Booking Commitment Fee | **Not implemented** |
| Non-refundable engagement payment | **Not implemented** |
| Messaging access unlock | **Not implemented** (chat is open post-order) |
| Caregiver availability reservation | **Not implemented** |

The current flow jumps straight from gig selection to full payment. There is no intermediate "unlock" step.

---

### Stage 2: Structured Negotiation

| Requirement | Status |
|---|---|
| Controlled parameters for negotiation | **Partially implemented** --- schedule, service address, tasks are structured |
| Client selects schedule/duties/duration/budget | **Partially implemented** --- `OrderTasks` has `PackageSelection`, `CareTasks`, `PreferredTimes` |
| Caregiver responds via structured options | **Partially implemented** --- `CaregiverContractGenerationDTO` uses structured schedule |
| Free-form contract writing disabled | **Mostly achieved** --- contracts are system-generated, though `AdditionalNotes` fields allow free text |

The `OrderTasks` service is a strong foundation. It captures package selection, care tasks with categories and priorities, special instructions, preferred times, and emergency contacts. Pricing is estimated server-side.

**Gap:** The negotiation happens *after* full payment, not as a pre-payment structured flow. The review recommends negotiation *before* the main payment, funded only by a commitment fee.

---

### Stage 3: Contract Approval + Escrow

| Requirement | Status |
|---|---|
| Platform auto-generates contract | **Implemented** --- LLM-based generation from structured data |
| Payment converts to escrow on approval | **Not implemented** |
| Escrow release on service completion | **Not implemented** |
| Balance collection after approval | **Not implemented** |

The `CaregiverWallet` entity tracks `TotalEarned`, `WithdrawableBalance`, and `PendingBalance`, which is architecturally suitable for escrow. However, there is no automatic escrow hold triggered by contract approval, and no automatic release triggered by service verification.

Currently, on order creation, the `ClientOrderService` credits the caregiver wallet immediately (via `walletService` and `ledgerService` injected into the service). The review recommends funds be held in escrow until service delivery is verified.

---

## 4. Contract Architecture --- Current State

### A. Platform Agreement (Always Active)

**Status: Not implemented in the system.**

There is no `PlatformAgreement` entity or service. The review requires:
- Client ↔ Platform terms
- Caregiver ↔ Platform terms
- Non-circumvention clauses
- Payment handling terms
- Communication monitoring consent

None of these are tracked or enforced in the backend. Acceptance of platform terms is not recorded.

---

### B. Care Service Agreement (Per Booking)

**Status: Implemented.**

The `Contract` entity covers the per-booking agreement with:
- Order reference, gig reference, party IDs
- Package selection and tasks
- Generated terms (LLM)
- Schedule with day/time slots
- Service address, requirements, access instructions
- Negotiation tracking (rounds, history)
- Approval/rejection timestamps

**Missing clauses from the review:**
- Escalation protocol (what happens in a medical emergency)
- Explicit cancellation policy terms
- Liability limitations
- Reporting requirements (visit logs, care notes)

---

## 5. Invoice Architecture --- Current State

### Invoice Type A: Engagement Invoice

**Status: Not implemented.** There is no concept of an engagement-only invoice. The `BillingRecord` is created after full payment and order creation.

### Invoice Type B: Service Invoice

**Status: Partially implemented.** The `BillingRecord` entity captures:
- Order ID, client/caregiver/gig references
- Amount paid, order fee, service charge, gateway fees
- Period start/end, billing cycle number
- Service type (one-time or monthly)
- Payment transaction reference and status

However, it does not include finalized duty lists, hourly/daily rate breakdowns, or escrow-specific fields.

### Invoice Type C: Settlement Invoice

**Status: Not implemented.** There is no periodic settlement invoice generation. The `EarningsLedger` provides a transaction-level audit trail, and the `CaregiverWallet` tracks balances, but there is no aggregated settlement report or invoice generation for caregivers.

**Nigeria-specific note:** Nigerian tax law (Finance Act 2023 amendments) requires platforms facilitating transactions to maintain adequate records. A proper invoice trail is not just operationally useful---it is increasingly a regulatory obligation for digital platforms.

---

## 6. Required System Fields --- Gap Analysis

### Caregiver Profile

| Field | Status | Location |
|---|---|---|
| ID verification status | **Exists** | `Verification` entity (`IsVerified`, `VerificationStatus`) |
| Certification ID | **Exists** | `Certification` entity |
| Background check status | **Partially** | Dojah document verification exists; no dedicated criminal background check |
| Assessment score | **Exists** | `Assessment` entity, `AssessmentSession`, scoring logic in `EligibilityService` |
| Service categories | **Exists** | `ServiceRequirement` entity, tiered eligibility system |

### Gig

| Field | Status | Location |
|---|---|---|
| Service scope | **Exists** | `Gig.Title`, `Gig.Category`, `Gig.SubCategory` |
| Pricing model | **Basic** | `Gig.Price` (single integer); no multi-tier pricing on the gig itself |
| Availability schedule | **Not on gig** | Schedule is captured at contract level, not gig level |
| Geographic coverage | **Not implemented** | `Location` entity exists but is not linked to gig visibility |
| Allowed duty checklist | **Not on gig** | Duties defined at `OrderTasks` level, not on the gig template |

### Contract Object

| Field | Status |
|---|---|
| Contract ID | **Exists** |
| Negotiated parameters | **Exists** (schedule, address, requirements) |
| Risk disclaimers | **Not implemented** |
| Emergency contact | **Exists** at `OrderTasks` level, not on contract |
| Approval timestamps | **Exists** (`ClientApprovedAt`, `SubmittedAt`, etc.) |

### Payment/Escrow

| Field | Status |
|---|---|
| Escrow balance | **Not implemented** (wallet has `PendingBalance` but no escrow semantics) |
| Payout schedule | **Not implemented** (payouts are manual via `WithdrawalRequest`) |
| Dispute status | **Exists** (`HasDispute`, `DisputeReason` on `ClientOrder`) |
| Refund eligibility flag | **Not implemented** |

---

## 7. Anti-Leak Controls --- Current State

| Control | Status | Detail |
|---|---|---|
| Phone/email auto-blocking in chat | **Not implemented** | `ContentSanitizer` handles XSS only |
| External link filtering | **Not implemented** | No URL detection or blocking |
| Non-circumvention clause | **Not implemented** | No T&C acceptance tracking |
| Platform-only payments enforcement | **Partially** | Payments go through Flutterwave; no mechanism to detect off-platform transfers |
| Communication logging | **Implemented** | `ChatMessage` entities stored in MongoDB |

**Priority for Nigeria:** This is arguably the highest-risk gap. Nigerian users are accustomed to WhatsApp-based transactions. Without active phone number pattern detection (e.g., regex for `0[789][01]\d{8}` and `+234`), email pattern detection, and social media handle blocking, the platform will haemorrhage users after first contact. Industry benchmarks from similar Nigerian platforms (e.g., domestic worker platforms like SweepSouth Nigeria, Eden Life) show 30-50% off-platform leakage without active controls.

---

## 8. Nigeria-Specific Operational Risks --- Current Mitigation

### Trust Deficit

**Current mitigation: Moderate.**
- Dojah-based identity verification is live
- Assessment/certification gating for specialised categories (MedicalSupport, etc.)
- Review system exists (`Review` entity)

**Missing:** Progressive commitment payments, verified badge system, guarantor/reference collection (common Nigerian trust signal).

### Diaspora Oversight Expectations

**Current mitigation: Minimal.**
- The notification system (`NotificationHub`, `Notification` entity, `NotificationPreferences`) provides real-time alerts
- `ClientRecommendation` entity exists for matching

**Missing:** Visit verification reports, geotagged check-in/check-out, photo evidence of care, scheduled transparency reports for remote family members. For the Nigerian diaspora market (estimated at 15-17 million Nigerians abroad), this is a critical retention driver.

### Informal Workforce Reality

**Current mitigation: Moderate.**
- Caregiver bank account collection (`CaregiverBankAccount`)
- Wallet system for digital earnings management
- Withdrawal request flow with admin verification

**Missing:** Explicit contractor classification in system contracts, NIN (National Identification Number) collection beyond basic Dojah verification, NHIS (National Health Insurance Scheme) compliance for medical care categories.

### Transportation Uncertainty

**Current mitigation: Not addressed.**
- No flexible cancellation windows
- No lateness grace period
- No transportation cost factoring in pricing

**Nigeria-specific context:** Traffic conditions in Lagos, Abuja, and Port Harcourt are notoriously unpredictable. A caregiver scheduled for 9:00 AM in Lekki coming from Ajah may face 30-90 minute delays depending on traffic. The platform needs schedule flexibility that accounts for this reality --- rigid penalty systems will drive caregiver churn.

---

## 9. Additional Risks --- Current Coverage

| Risk | Current Coverage |
|---|---|
| Caregiver safety incidents | **Not implemented** --- no panic button, no incident reporting system |
| Medical escalation liability | **Not addressed** --- no emergency protocol, no medical disclaimer enforcement |
| Data privacy breaches | **Partially** --- IDOR protection on most endpoints, JWT-based auth, input sanitisation; no NDPR compliance audit |
| Payment fraud attempts | **Good** --- amount mismatch detection, webhook signature verification, duplicate payment guards, idempotency checks |
| Caregiver churn after first booking | **Not tracked** --- no retention analytics, no loyalty/incentive system |
| Long-term off-platform migration | **High risk** --- no anti-leak controls as detailed in Section 7 |

---

## 10. Strategic Positioning --- Assessment

The design review positions CarePro as a **"Trust Infrastructure Marketplace"**. The current implementation aligns with this in several ways:

**Aligned:**
- Verification pipeline (Dojah integration)
- Assessment-gated service categories
- Server-side pricing (no client/caregiver price manipulation)
- Structured contract generation
- Financial audit trail (wallet + ledger)
- Payment security controls

**Not yet aligned:**
- No escrow (undermines "payment protection" claim)
- No anti-leak controls (undermines "enforcement" claim)
- No visit verification (undermines "accountability" claim)
- No platform agreement/T&C enforcement layer

---

## 11. Engineering Priority Mapping

The design review recommends 6 engineering priorities. Here is the current state of each:

| Priority | Review Recommendation | Current State | Effort Estimate |
|---|---|---|---|
| 1 | Contract generation engine (template-based) | **Partially done** --- LLM-based generation exists; needs template locking for mandatory clauses | Medium |
| 2 | Escrow wallet implementation | **Not done** --- wallet architecture exists but operates as direct-credit, not escrow | High |
| 3 | Structured negotiation UI | **Backend partially ready** --- `OrderTasks` + `ContractGeneration` DTOs provide structured data; frontend integration and pre-payment negotiation flow needed | Medium |
| 4 | Messaging compliance filters | **Not done** --- only XSS sanitisation; needs phone/email/link detection | Medium |
| 5 | Multi-stage invoice generation | **Not done** --- single `BillingRecord` exists; needs Engagement, Service, and Settlement invoice types | Medium |
| 6 | Care activity verification system | **Designed** --- full specification in Section 12 (check-in/check-out, GPS, hours budget, overcharge detection, client checklist, caregiver logging); implementation pending | High |

---

## 12. Service Verification & Activity Logging System (Gap Closure Design)

**Status: Not implemented.** This section defines the full specification for closing the "Care Activity Verification" gap identified in the scorecard. It covers service logging, automated hours calculation, check-in/check-out with GPS, client completion checklists, and overcharge detection.

---

### 12.1 Hours Calculation Model

The platform must auto-generate expected hours based on the client's purchased service package. The rules are derived from the existing `PackageSelection.VisitsPerWeek` and `Subscription.FrequencyPerWeek` fields.

#### Core Assumptions

| Parameter | Rule |
|---|---|
| One visit = one service day | Each visit in the `VisitsPerWeek` contract field represents one full service day |
| Minimum care hours per visit | **4 hours** (the minimum to qualify as a full-day service) |
| Maximum care hours per visit | **6 hours** (hard cap per single visit) |
| Transportation allowance | **1 hour** added per visit (not billable as care time, but factored into caregiver daily commitment) |
| Total caregiver commitment per visit | 5 hours minimum (4 care + 1 transport) to 7 hours maximum (6 care + 1 transport) |

#### Monthly Hours Budget Calculation

The system must auto-calculate the monthly hours budget when a contract is approved or a subscription billing cycle begins.

**Formula:**

```
Monthly visits        = VisitsPerWeek × 4 (weeks per billing cycle)
Expected care hours   = Monthly visits × 4 (minimum hours per visit)
Maximum care hours    = Monthly visits × 6 (maximum hours per visit)
Transport hours       = Monthly visits × 1
Total caregiver hours = Maximum care hours + Transport hours
```

**Worked examples:**

| Package | Visits/Week | Monthly Visits | Min Care Hours/Month | Max Care Hours/Month | Transport Hours/Month | Total Max Commitment |
|---|---|---|---|---|---|---|
| 1x per week | 1 | 4 | 16 hrs | 24 hrs | 4 hrs | 28 hrs |
| 2x per week | 2 | 8 | 32 hrs | 48 hrs | 8 hrs | 56 hrs |
| 3x per week | 3 | 12 | 48 hrs | 72 hrs | 12 hrs | 84 hrs |
| 5x per week | 5 | 20 | 80 hrs | 120 hrs | 20 hrs | 140 hrs |

These values should be stored on the contract/subscription at creation time and used as the reference baseline for overcharge detection.

---

### 12.2 Overcharge Detection Logic

An overcharge occurs when a caregiver logs more time than the contract allows. The system must flag this automatically.

#### Per-Visit Overcharge

Triggered when a single check-in/check-out session exceeds 6 hours of care time.

```
If (checkout_time - checkin_time - transport_allowance) > 6 hours:
    overcharge_hours = actual_care_hours - 6
    flag: VISIT_OVERCHARGE
```

#### Monthly Overcharge

Triggered when total logged care hours in a billing cycle exceed the maximum monthly budget.

```
If (sum of all care hours in current cycle) > (VisitsPerWeek × 4 weeks × 6 hours):
    overcharge_hours = total_logged - max_care_hours
    flag: MONTHLY_OVERCHARGE
```

#### Nigeria-Specific Considerations for Overcharge

- **Traffic-adjusted transport:** In Lagos, Abuja, and Port Harcourt, the 1-hour transport allowance may be insufficient. The system should allow admin-configurable transport allowances per city/LGA (Local Government Area). Default: 1 hour. Lagos Island to Mainland routes: consider 1.5--2 hours.
- **Public holiday adjustments:** Nigerian public holidays (over 15 per year including state-specific ones) may affect service schedules. The system should not flag missed visits on recognised public holidays.
- **Harmattan/rainy season:** Extreme weather periods (November--February for Harmattan, June--September for heavy rains) cause widespread transportation disruption. Consider seasonal transport allowance adjustments.

---

### 12.3 Caregiver Check-In / Check-Out System

This is the core mechanism for verifying that care was actually delivered, when, and where.

#### Check-In Flow

1. Caregiver opens the app on a scheduled service day
2. Presses "Check In" button (only available within a configurable window of their scheduled start time, e.g., ±30 minutes)
3. System captures:
   - **Timestamp** (UTC, displayed in WAT --- West Africa Time, UTC+1)
   - **GPS coordinates** (latitude, longitude)
   - **Accuracy radius** of the GPS reading (to flag unreliable readings)
   - **Device ID** (to detect multi-device fraud)
4. System validates GPS against the service address on the contract
5. If GPS is within acceptable radius (configurable, default 500 metres), check-in is confirmed
6. If GPS is outside radius, check-in is flagged for review but not blocked (to account for GPS drift in dense Nigerian urban areas like Surulere, Ikeja, Wuse)

#### Check-Out Flow

1. Caregiver presses "Check Out" at end of service
2. Same data captured: timestamp, GPS, accuracy, device ID
3. System auto-calculates:
   - **Gross hours** = check-out time − check-in time
   - **Care hours** = gross hours − transport allowance (1 hour)
   - **Status**: Normal (4--6 hrs), Under-time (< 4 hrs), Over-time (> 6 hrs)
4. If under-time: flag for client review ("Caregiver checked out early")
5. If over-time: flag for overcharge review

#### GPS Validation Rules

| Scenario | Action |
|---|---|
| GPS within 500m of service address | Auto-confirm location |
| GPS 500m--2km from service address | Accept with warning flag; notify client |
| GPS > 2km from service address | Flag as location mismatch; require caregiver to add a note explaining (e.g., "took care recipient to hospital") |
| GPS unavailable / denied | Allow check-in but flag as "unverified location"; require photo proof |
| Mock/spoofed GPS detected | Block check-in; alert admin (use device sensor cross-referencing where available) |

**Nigeria-specific GPS considerations:**
- Many Nigerian residential addresses lack formal geocoding. The platform should allow clients to drop a pin on a map during contract setup rather than relying on text address geocoding.
- GPS accuracy in high-density areas (Oshodi, Computer Village Ikeja, Mile 12 market areas) may degrade to 50--100m. The 500m default radius accounts for this.
- Indoor GPS drift in multi-story buildings (common in Lagos highrise estates like 1004, Eko Atlantic) may require Wi-Fi-assisted positioning.

---

### 12.4 Service Day Log (Caregiver Logging)

For each checked-in visit, the caregiver must log what was done. This creates the audit trail for the client completion checklist and settlement invoices.

#### Required Log Fields per Visit

| Field | Type | Required | Description |
|---|---|---|---|
| Visit ID | Auto-generated | Yes | Unique identifier linked to contract + date |
| Contract ID | Reference | Yes | Which contract this visit belongs to |
| Subscription ID | Reference | If recurring | Which billing cycle |
| Billing Cycle Number | Integer | If recurring | e.g., Cycle 3 of monthly subscription |
| Check-In Time | DateTime | Yes | From check-in action |
| Check-Out Time | DateTime | Yes | From check-out action |
| Check-In GPS | Coordinates | Yes | Lat/long + accuracy |
| Check-Out GPS | Coordinates | Yes | Lat/long + accuracy |
| Gross Hours | Decimal | Auto-calculated | Total time on site |
| Care Hours | Decimal | Auto-calculated | Gross minus transport allowance |
| Transport Hours Used | Decimal | Auto-calculated | Capped at allowance |
| Tasks Completed | List | Yes | Caregiver selects from contract task list |
| Tasks Not Completed | List | Conditional | If any task was skipped, reason required |
| Care Notes | Text | Optional | General notes about the visit |
| Incidents | Text | Conditional | Any falls, medical events, behavioural changes |
| Care Recipient Condition | Enum | Yes | "Stable", "Improved", "Declined", "Requires Attention" |
| Photo Evidence | Image(s) | Optional | Meal preparation, medication administered, etc. |
| Visit Status | Enum | Auto | "Completed", "Partial", "Flagged", "Disputed" |

#### Auto-Hours Generation

When a caregiver fails to check out (app crash, phone died, forgot), the system must auto-generate hours to prevent stalled records:

| Scenario | Auto Action |
|---|---|
| No check-out within 8 hours of check-in | Auto-checkout at scheduled end time; flag as "auto-closed" |
| No check-in on a scheduled day | Mark as "Missed Visit"; notify both client and caregiver; log as 0 hours |
| Check-in but caregiver goes offline | After 2 hours of no GPS signal, send push notification; after 4 hours, auto-checkout |
| Duplicate check-in same day | Block second check-in; only one session per scheduled visit |

---

### 12.5 Client Service Completion Checklist

Before a service period (or individual visit, depending on configuration) is marked as "Completed", the client must confirm via a structured checklist. This is the counterpart to the caregiver's log and is the trigger for escrow release.

#### Per-Visit Confirmation (Client Reviews Each Visit)

The client receives a notification after each caregiver check-out and must respond within a configurable window (default: 48 hours; if no response, auto-approved).

| Checklist Item | Type | Required |
|---|---|---|
| Was the caregiver present? | Yes / No | Yes |
| Did the caregiver arrive approximately on time? | Yes / No / Not Sure | Yes |
| Were all agreed tasks completed? | Yes / Partially / No | Yes |
| If partially or no: which tasks were missed? | Multi-select from contract tasks | Conditional |
| Rate the quality of care provided | 1--5 stars | Yes |
| Any concerns or incidents to report? | Free text | Optional |
| Would you like to flag this visit for review? | Yes / No | Yes |

#### Monthly / Billing Cycle Completion Checklist (Required Before Settlement)

At the end of each billing cycle (or on-demand for one-time services), the client completes a summary review:

| Checklist Item | Type | Required |
|---|---|---|
| Total visits expected this period | Auto-displayed (from contract) | Read-only |
| Total visits completed | Auto-displayed (from check-in logs) | Read-only |
| Total visits missed | Auto-displayed | Read-only |
| Total care hours logged | Auto-displayed | Read-only |
| Do you confirm the logged hours are accurate? | Yes / No / Dispute | Yes |
| If dispute: specify which visits and reason | Free text per visit | Conditional |
| Overall satisfaction this period | 1--5 stars | Yes |
| Continue service next period? | Yes / No / Pause | Yes (for recurring) |
| Any tasks to add/remove for next period? | Free text | Optional |
| Approve payment release for this period? | Yes / No / Partial | Yes |

#### Completion Status Rules

| Client Response | System Action |
|---|---|
| All visits confirmed, payment approved | Mark period as "Completed"; release escrow; generate Settlement Invoice |
| Partial approval | Release approved portion; hold remainder; open dispute for flagged visits |
| Disputes raised | Freeze escrow for disputed visits; notify admin; enter dispute resolution |
| No response within 48 hours (per visit) | Auto-approve the individual visit |
| No response within 7 days (billing cycle) | Auto-approve the full cycle; release escrow; send reminder at 3 days and 6 days |

**Nigeria-specific note:** Many Nigerian clients (especially elderly or less tech-savvy in-country family members acting on behalf of diaspora payers) may not respond to digital checklists promptly. The auto-approval windows must be generous, and the platform should support:
- SMS/USSD-based checklist confirmation (for clients without smartphones or reliable internet)
- WhatsApp Business API notification with quick-reply buttons (leveraging Nigeria's dominant messaging platform)
- Designated family member proxy approval (diaspora payer's relative in Nigeria can confirm on their behalf)

---

### 12.6 Service Verification Summary Dashboard

Both clients and caregivers need visibility into service delivery data. The following views should be made available:

#### Client View

- Calendar view of scheduled vs. completed visits
- Running total: hours used vs. hours in budget
- Overcharge alerts (if any)
- Pending visits awaiting their confirmation
- Link to raise dispute on any visit

#### Caregiver View

- Calendar view of upcoming and completed visits
- Running total: hours logged this cycle
- Earnings projection based on logged hours
- Flagged visits requiring their attention (missed check-out, location mismatch, etc.)

#### Admin View

- All flagged visits across the platform
- Overcharge reports
- GPS mismatch logs
- Missed visit patterns (detect caregiver absenteeism)
- Auto-closed sessions report
- Settlement approval queue

---

### 12.7 Integration with Existing Architecture

The service verification system connects to existing entities and fills the gap between payment and completion.

```
Payment Flow (exists)          Verification Flow (new)           Settlement Flow (partially exists)
─────────────────────          ──────────────────────            ──────────────────────────────────
Client pays                    Contract approved                 Client confirms checklist
  → PendingPayment               → Monthly hours budget set       → Per-visit confirmation
  → ClientOrder created           → Schedule locked                → Billing cycle review
  → BillingRecord                                                  → Escrow release trigger
  → CaregiverWallet             Caregiver checks in               → Settlement Invoice generated
    (currently: direct credit)    → GPS captured                   → CaregiverWallet credited
    (should be: escrow hold)      → Timestamp logged               → EarningsLedger entry
                                  → Visit log opened
                                
                                Caregiver checks out
                                  → Hours auto-calculated
                                  → Tasks logged
                                  → Overcharge evaluated
                                  → Client notified
```

#### Entity Dependencies

| New Concept | Connects To |
|---|---|
| ServiceVisitLog (new entity) | `Contract.Id`, `Subscription.Id`, `ClientOrder.Id` |
| CheckInRecord (new entity) | `ServiceVisitLog.Id`, GPS data |
| ClientVisitConfirmation (new entity) | `ServiceVisitLog.Id`, checklist responses |
| MonthlyServiceSummary (new entity) | `Subscription.Id`, `BillingRecord.Id`, aggregated hours |
| OverchargeFlag (new entity) | `ServiceVisitLog.Id` or `MonthlyServiceSummary.Id` |
| HoursBudget (new fields on Contract/Subscription) | `PackageSelection.VisitsPerWeek`, calculation rules from 12.1 |

#### Existing Fields That Support This

- `Contract.Schedule` (list of `ScheduledVisit` with day/time) --- defines when check-ins should happen
- `PackageSelection.VisitsPerWeek` --- determines monthly visit count and hours budget
- `ScheduledVisit.StartTime` / `EndTime` --- validates check-in timing window
- `ClientTask` list on `Contract` --- provides the task checklist for caregiver logging
- `CaregiverWallet.PendingBalance` --- can be repurposed as escrow hold until client confirmation
- `EarningsLedger` --- records fund movements on confirmation/dispute

---

### 12.8 Overcharge Billing Scenarios (Detailed)

When an overcharge is detected, the platform must handle it transparently.

#### Scenario A: Single Visit Over 6 Hours

> A caregiver on a 2x/week contract checks in at 08:00 and checks out at 15:30 (7.5 gross hours).

| Calculation | Value |
|---|---|
| Gross hours | 7.5 hrs |
| Less transport allowance | −1.0 hr |
| Actual care hours | 6.5 hrs |
| Maximum care hours per visit | 6.0 hrs |
| **Overcharge** | **0.5 hrs** |

**System action:** Flag the visit. Notify client: "Caregiver logged 6.5 care hours on this visit. Your plan covers up to 6 hours per visit. Please confirm if the extra 0.5 hours was requested." Client can approve (overcharge is billable) or dispute.

#### Scenario B: Monthly Budget Exceeded

> A caregiver on a 2x/week contract (max 48 care hours/month) has logged 46 hours across 7 visits. On the 8th visit, they log 5 care hours (total: 51 hours).

| Calculation | Value |
|---|---|
| Monthly max care hours | 48 hrs (2 visits × 4 weeks × 6 hrs) |
| Hours logged after 8th visit | 51 hrs |
| **Monthly overcharge** | **3 hrs** |

**System action:** Flag the billing cycle. At cycle-end summary, show client: "Total care hours this month: 51 hrs. Your plan covers 48 hrs. Overcharge: 3 hrs." Calculate overcharge fee at the contract's hourly rate. Client can approve (added to next billing) or dispute.

#### Scenario C: Under-Delivery

> A caregiver on a 2x/week contract completes only 6 of 8 expected visits in a month.

| Calculation | Value |
|---|---|
| Expected visits | 8 |
| Completed visits | 6 |
| Missed visits | 2 |
| Hours shortfall | 8--12 hrs (depending on visit length) |

**System action:** At cycle-end summary, show client the shortfall. Options: (a) Pro-rated credit applied to next cycle, (b) Extend current cycle to make up visits, (c) Raise a formal dispute.

---

## Summary Scorecard

| Area | Maturity (1-5) | Risk Level |
|---|---|---|
| Caregiver onboarding & verification | 4 | Low |
| Assessment & eligibility gating | 4 | Low |
| Gig management | 3 | Medium |
| Payment collection | 4 | Low |
| Contract generation | 3 | Medium |
| Escrow / fund holding | 1 | **Critical** |
| Messaging & anti-leak | 1 | **Critical** |
| Invoice architecture | 2 | High |
| Platform legal agreements | 1 | **Critical** |
| Nigeria-specific adaptations | 2 | High |
| Dispute resolution | 2 | High |
| Care activity verification | 1 → **Designed** | **Critical → High** (design complete, implementation pending) |

**Overall assessment:** The platform has solid foundational infrastructure for identity, payments, and basic marketplace operations. The critical gaps are in **escrow mechanics**, **anti-leak controls**, **platform legal agreements**, and **care activity verification** --- all of which are essential for operating a trust-based care marketplace in the Nigerian context where personal trust often supersedes platform trust, and where regulatory frameworks (NDPR, FCCPA, Finance Act) are increasingly being enforced.

The **Service Verification System** (Section 12) now provides a complete design specification covering check-in/check-out with GPS, automated hours budgeting, overcharge detection, caregiver service logging, and client completion checklists. This design integrates with the existing `Contract`, `PackageSelection`, `CaregiverWallet`, and `EarningsLedger` entities and closes the most significant operational gap identified in this analysis.

---

*End of Analysis*
