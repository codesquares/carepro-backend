# Discovery Findings — Current Referral System Audit

All findings below are **confirmed by reading the code** unless marked *(inferred)*. File:line references included.

## 1. Current referrer creation flow

**Confirmed — admin-only, no public path exists.**

- Endpoint: `POST /api/referrals/referrers` in [ReferralsController.cs:20-29](../CarePro-Api/Controllers/Content/ReferralsController.cs#L20-L29), guarded by `[Authorize(Policy = "ReferralManagementPolicy")]`.
- Policy definition ([Program.cs:402-406](../CarePro-Api/Program.cs#L402-L406)): requires `SuperAdmin` role, OR (`Admin` role AND `department` claim == `Finance`). So it's not just "any admin" — specifically Finance-department admins or SuperAdmin.
- Fields captured (`CreateReferrerRequest`, [ReferralDTOs.cs:3-9](../Application/DTOs/ReferralDTOs.cs#L3-L9)): `FullName`, `Email`, `PhoneNo` (optional), and an optional nested `BankAccount` (FullName, BankName, AccountNumber, AccountName).
- Validation in `ReferralService.CreateReferrerAsync` ([ReferralService.cs:35-80](../Infrastructure/Content/Services/ReferralService.cs#L35-L80)): `FullName` and `Email` required (non-whitespace); email is lowercased/trimmed and checked against existing referrers (app-level check) before insert. There is **also** a DB-level unique index on `Referrer.Email` ([CareProDbContext.cs:161](../Infrastructure/Content/Data/CareProDbContext.cs#L161)), so email uniqueness is enforced twice (belt and suspenders).
- Referral code is **not** created in the same call — it's a separate step: `POST /api/referrals/codes` with `{ referrerId }`, which an admin calls after creating the referrer (per [FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md:311-317](../FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md#L311-L317), the frontend fetches the referrer list to get the ID for this second call).

**No public/half-built signup path exists.** I searched the whole codebase (`grep` across all `.cs` files for referrer/referral + apply/application/signup/become) and the only public-facing referral endpoints are checkout-time *code redemption* (`ValidateReferralForCheckoutAsync`, called from `PendingPaymentService.cs:322`) — that's a client entering someone else's code at checkout, not a referrer applying to become one. There's nothing named `ReferrerApplication`, no `[AllowAnonymous]` anywhere in `ReferralsController.cs`, and no other controller touches `Referrer`. This is genuinely new work, not a resurrection of something partial.

## 2. Current code generation logic

**Confirmed — format has NOT changed since your earlier work; still date+random-suffix.**

- `GenerateReferralCode()` ([ReferralService.cs:350-354](../Infrastructure/Content/Services/ReferralService.cs#L350-L354)):
  ```csharp
  var shortCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
  return $"CAREPRO-REF-{DateTime.UtcNow:yyyyMMdd}-{shortCode}";
  ```
  Produces e.g. `CAREPRO-REF-20260721-A1B2C3` — a 6-char hex slice of a GUID, not a "4-digit random suffix." So today's suffix is 6 alphanumeric hex characters, longer than what you're planning to move to.

- **Uniqueness enforcement is real DB-level, not just generation-time check.** Two layers:
  1. Generation-time collision loop in `CreateReferralCodeAsync` ([ReferralService.cs:91-96](../Infrastructure/Content/Services/ReferralService.cs#L91-L96)): `do { code = GenerateReferralCode(); } while (await _dbContext.ReferralCodes.AnyAsync(rc => rc.Code == code));`
  2. A genuine unique index on `ReferralCode.Code` in the DB context ([CareProDbContext.cs:171](../Infrastructure/Content/Data/CareProDbContext.cs#L171)): `modelBuilder.Entity<ReferralCode>().HasIndex(rc => rc.Code).IsUnique();`

  So this is already the safe pattern (app-side pre-check + DB constraint as final backstop) — a new alias-based generator should follow the same shape.

## 3. Admin referrer management — what exists today

**Confirmed — API-only; no admin dashboard UI lives in this repo (it's backend-only).**

- List endpoints exist: `GET /api/referrals/referrers` and `GET /api/referrals/referrers/list` (identical, the second is a cache-busting fallback route) — both return `ReferrerListItem` (id, fullName, email, phoneNo, createdAt). [ReferralsController.cs:42-56](../CarePro-Api/Controllers/Content/ReferralsController.cs#L42-L56)
- Redemption tracking: `GET /api/referrals/redemptions?startDate&endDate` and `POST /api/referrals/redemptions/{id}/mark-paid`. Plus an XLSX export at `GET /api/admin/export/referral-redemptions` ([AdminExportService.cs:245](../Infrastructure/Content/Services/AdminExportService.cs#L245), policy: `AnalyticsPolicy`).
- Per [FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md:464](../FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md#L464) (dated 2026-07-11), the frontend checklist item #7 says finance admin UI screens for referrer/code/redemption still needed to be *built* on the frontend side using these APIs — so as of that handoff, there was no dashboard, just the API contract. *(This repo is API-only — I can't confirm whether the frontend team has since built that screen; worth asking them directly.)*

**Entity fields today, relevant to a "pending applicant" state:**
- `Referrer` ([Referrer.cs](../Domain/Entities/Referrer.cs)): Id, FullName, Email, PhoneNo, CreatedAt. **No status field at all** — every `Referrer` row is implicitly "approved" today; there's no concept of pending/rejected.
- `ReferrerBankAccount` ([ReferrerBankAccount.cs](../Domain/Entities/ReferrerBankAccount.cs)): separate collection, 1:1 with a `ReferrerId` (unique index), optional at creation time today.
- `ReferralRedemption` ([ReferralRedemption.cs](../Domain/Entities/ReferralRedemption.cs)): tracks payout lifecycle per redemption (`PayoutStatus`: Pending/Paid), not per-referrer.

So a "pending applicant, not yet approved" state genuinely doesn't exist anywhere in the current model — the `Referrer` entity has no status/approval concept to extend. This will need a new field (or a separate `ReferrerApplication` entity) — a real design decision, not a small addition to existing code.

## 4. Email capability for sending referral codes

**Confirmed — no existing "send code" email; would be a new email type on the existing shared transport.**

- There is **no** current flow that emails a referrer their code. `CreateReferralCodeAsync` ([ReferralService.cs:82-111](../Infrastructure/Content/Services/ReferralService.cs#L82-L111)) does not call `_emailService` at all — code generation is silent today. The only referral-related email that exists is a *redemption* notification ("your code was used") sent from `CreateRedemptionAsync` via `SendSystemNotificationEmailAsync` ([ReferralService.cs:257-277](../Infrastructure/Content/Services/ReferralService.cs#L257-L277)) — that's a different trigger (someone used the code), not "here is your new code."
- Transport confirmed: `EmailService` ([EmailService.cs:24](../Infrastructure/Services/EmailService.cs#L24)) sends via MailKit `SmtpClient` over StartTls ([EmailService.cs:845-872](../Infrastructure/Services/EmailService.cs#L845-L872)), authenticated with `MailSettings` (which per your Brevo migration memory now carries Brevo's SMTP relay credentials via env vars). So yes — this is a single centralized SMTP send path already wired to Brevo; a new "here's your referral code" email just needs a new method added to `IEmailService`/`EmailService` (or reuse `SendGenericNotificationEmailAsync`/`SendCustomEmailToUserAsync`, which already exist as generic subject+content senders) — no new transport/infrastructure needed.

## 5. Alias collision handling — flagged, not decided

**This is unresolved and needs your product call before design.** Nothing in the current code hints at an answer since aliases don't exist yet — no precedent to infer from.

The two options, spelled out:
- **Reject duplicate alias** — requires a new unique index on the alias field itself (or app-level pre-check), and requires deciding what "duplicate" means (case-sensitive? trimmed/normalized?). Simpler mental model for users/admins scanning a list, but means aliases become a scarce resource ("drwealth" is gone forever once claimed).
- **Allow duplicate alias, rely on full code being unique** — matches the existing pattern exactly (uniqueness is enforced on the *generated code*, not any input to it — see #2 above), so it's less new code. But you'd end up with two different people both holding codes like `drwealth0452` and `drwealth7710`, which could confuse an admin scanning the list or a client who's seen one "drwealth" code shared publicly and isn't sure which is real. Also matters for the collision loop: if aliases can repeat, the 4-digit suffix space (10,000 combinations) is shared *per alias*, so a popular alias could realistically exhaust or slow down collision retries over time — worth knowing before committing to 4 digits specifically.

Flagging explicitly per your instruction — I have not assumed an answer either way, and the uniqueness-check code (alias field vs. code field) depends entirely on which you pick.

## Everything else — confirmed context useful for the design pass

- The full existing code format, generation, and both endpoints (`POST /api/referrals/referrers`, `POST /api/referrals/codes`) are documented with request/response shapes in [FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md §3.4](../FRONTEND-REFERRAL-COMMITMENT-CHANGE-NOTE.md#L254-L346) — useful as a existing-contract reference when scoping the new public signup + admin approval endpoints, since frontend already has patterns for consuming this API family.
- `ReferralSettings.PayoutAmount` defaults to 5000m and is referenced via `IOptions<ReferralSettings>` — confirms settings for this domain are already externalized, a pattern any new "pending applicant" feature flags/settings should follow.
