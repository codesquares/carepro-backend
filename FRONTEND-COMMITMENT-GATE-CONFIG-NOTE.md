# Frontend Note: Commitment Gate Runtime Config

## Endpoint
- GET /api/public-config

## Response Field
- commitmentGateEnabled: boolean

## Meaning
- true: commitment gate is ON (current behavior). Frontend should keep commitment-required UI/guards active.
- false: commitment gate is OFF (pause mode). Frontend should remove commitment-required UI/guards for this period.

## Cache Guidance
- Recommended frontend cache TTL: 60 seconds.

## Fail-safe Behavior
- If this config endpoint is unreachable, frontend must default to ON behavior (treat commitmentGateEnabled as true).

## Scope
- This is a single global/environment-level backend flag, not per-user and not per-tenant.
