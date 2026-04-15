# Care Marketplace Platform --- Structural Review & Recommended Model

## Purpose

This document summarizes: - Structural vulnerabilities identified in the
current marketplace design - Recommended operational model adjustments -
Required contract and invoice architecture - Critical risk areas
specific to Nigeria/Africa markets - Required system fields and
safeguards

------------------------------------------------------------------------

## 1. Current Marketplace Flow (Observed)

### Caregiver

1.  Onboards
2.  Verification performed
3.  Assessment completed
4.  Creates service gigs

### Client

1.  Onboards
2.  Selects gig
3.  Pays platform before discussion
4.  Negotiates terms with caregiver
5.  Caregiver drafts contract
6.  Client approves / rejects / requests revision
7.  Refund or rematch if rejected

------------------------------------------------------------------------

## 2. Structural Vulnerabilities

### 2.1 Payment Before Defined Scope

**Risk** - Payment occurs before finalized service agreement. - Creates
dispute exposure and refund pressure. - Chargebacks likely from diaspora
payers.

**Impact** - Payment processor disputes - Legal ambiguity around
"consideration" - FX refund losses

------------------------------------------------------------------------

### 2.2 Caregiver-Authored Contracts

**Risk** - Inconsistent legal wording - Unauthorized medical promises -
Liability transfer to platform

**Impact** - Platform treated as facilitator of unsafe agreements -
Enforcement difficulty

------------------------------------------------------------------------

### 2.3 Off‑Platform Leakage (Disintermediation)

**Risk** Users exchange contact details during negotiation.

**Impact** - Revenue loss - Platform bypass - Long-term retention
failure

------------------------------------------------------------------------

### 2.4 Unlimited Negotiation Loop

**Risk** Clients negotiate repeatedly with multiple caregivers
risk-free.

**Impact** - Caregiver fatigue - Platform resource waste - Low
conversion rates

------------------------------------------------------------------------

### 2.5 Refund Abuse

**Risk** Full refund eligibility encourages speculative booking.

**Impact** - Payment gateway fee losses - Operational overhead

------------------------------------------------------------------------

### 2.6 Legal Classification Risk

Ambiguity between: - marketplace - agency - employer

Improper classification can create labor liability exposure.

------------------------------------------------------------------------

## 3. Recommended Marketplace Model

### Core Principle

Client initially pays for **Platform Engagement + Caregiver
Reservation**, not the care service itself.

------------------------------------------------------------------------

### Stage 1 --- Paid Unlock (Commitment Layer)

Client selects gig and pays a **Booking Commitment Fee**.

Unlocks: - messaging access - structured negotiation - caregiver
availability reservation - contract generation

Non-refundable.

------------------------------------------------------------------------

### Stage 2 --- Structured Negotiation

Negotiation occurs only through controlled parameters:

Client selects: - schedule - duties checklist - duration - start date -
location - budget range

Caregiver responds using structured options.

Free-form contract writing disabled.

------------------------------------------------------------------------

### Stage 3 --- Contract Approval + Escrow

Platform auto-generates contract.

After approval: - payment converts to escrow - or remaining balance
collected - service begins

------------------------------------------------------------------------

## 4. Required Contract Architecture

### A. Platform Agreement (Always Active)

Between: - Client ↔ Platform - Caregiver ↔ Platform

Must include: - non-circumvention clause - payment handling - dispute
mediation - contractor classification - communication monitoring consent

------------------------------------------------------------------------

### B. Care Service Agreement (Per Booking)

Between: - Client - Caregiver - Platform (facilitator)

Required clauses: - scope of care - service schedule - escalation
protocol - cancellation policy - liability limitations - reporting
requirements

------------------------------------------------------------------------

## 5. Invoice Architecture

### Invoice Type A --- Engagement Invoice

Issued immediately after payment.

Fields: - booking ID - caregiver ID - gig selected - unlock window
duration - platform fee - payment confirmation

Purpose: Legal justification for non-refundable payment.

------------------------------------------------------------------------

### Invoice Type B --- Service Invoice

Generated after contract approval.

Fields: - finalized duties - service dates - hourly/daily rate - escrow
amount - caregiver payout calculation

------------------------------------------------------------------------

### Invoice Type C --- Settlement Invoice

Issued periodically.

Fields: - completed visits - verified hours - platform commission -
caregiver earnings - total settlement

------------------------------------------------------------------------

## 6. Required System Fields

### Caregiver Profile

-   ID verification status
-   certification ID
-   background check status
-   assessment score
-   service categories

### Gig

-   service scope
-   pricing model
-   availability schedule
-   geographic coverage
-   allowed duty checklist

### Contract Object

-   contract ID
-   negotiated parameters
-   risk disclaimers
-   emergency contact
-   approval timestamps

### Payment/Escrow

-   escrow balance
-   payout schedule
-   dispute status
-   refund eligibility flag

------------------------------------------------------------------------

## 7. Critical Anti‑Leak Controls

-   Phone/email auto-blocking in chat
-   External link filtering
-   Non‑circumvention clause
-   Platform-only payments enforcement
-   Communication logging

------------------------------------------------------------------------

## 8. Nigeria‑Specific Operational Risks

### Trust Deficit

Users prioritize personal trust over platforms. Solution: Progressive
commitment payments.

### Diaspora Oversight Expectations

Families abroad require transparency. Solution: Visit reports +
verification logs.

### Informal Workforce Reality

Caregivers may lack formal employment structures. Solution: Strong
contractor classification clauses.

### Transportation Uncertainty

Strict scheduling penalties fail locally. Solution: Flexible
cancellation windows.

------------------------------------------------------------------------

## 9. Additional Risks To Monitor

-   Caregiver safety incidents
-   Medical escalation liability
-   Data privacy breaches
-   Payment fraud attempts
-   Caregiver churn after first booking
-   Long-term off-platform migration

------------------------------------------------------------------------

## 10. Strategic Positioning

Platform identity should remain:

**Trust Infrastructure Marketplace** ---not a home-care agency.

Primary value: - verification - enforcement - payment protection -
accountability

------------------------------------------------------------------------

## 11. Recommended Next Engineering Priorities

1.  Contract generation engine (template-based)
2.  Escrow wallet implementation
3.  Structured negotiation UI
4.  Messaging compliance filters
5.  Multi-stage invoice generation
6.  Care activity verification system

------------------------------------------------------------------------

End of Document
