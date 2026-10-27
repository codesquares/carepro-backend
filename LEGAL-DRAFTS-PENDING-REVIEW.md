# Contract wording — DRAFTS pending legal review

**Status: LOCAL ONLY. Do not deploy until a lawyer has approved every item below.**

All contract wording lives in one file: `Application/Content/ContractWording.cs`. The HTML contract
(`ContractTemplateService`), the PDF (`ContractPdfService`), the contract email and the client contract view all read
from it, so approved text is swapped in by editing that one file.

Contracts already generated before this change keep the wording they were created with (stored `GeneratedTerms` HTML);
the PDF is re-rendered from the current wording on download.

## Release checklist

| # | Where | Constant | Current (draft) text | Previous text | Reviewed |
|---|---|---|---|---|---|
| 1 | Subtitle | `Subtitle` | A CarePro Care Service Agreement | Facilitated through the CarePro Platform | ☐ |
| 2 | Section 1 intro (new sentence) | `PartiesIntro` | This agreement is between CarePro (the service provider) and the Client. CarePro has assigned the Caregiver named below to deliver the care service on CarePro's behalf. | *(none)* | ☐ |
| 3 | Section 1 table heading | `CaregiverColumnHeading` | Assigned Caregiver (delivering the service for CarePro) | Caregiver (Service Provider) | ☐ |
| 4 | Signature block label | `CaregiverSignatureLabel` | Assigned Caregiver | Caregiver | ☐ |
| 5 | Section 2 | `PlatformRelationshipText` | CarePro is a home-care service provider. CarePro enters into this agreement with the Client and delivers the agreed care package. CarePro assigns a vetted caregiver to deliver the service and remains responsible to the Client for the arrangement, subject to the terms of this agreement. The Caregiver acts on CarePro's behalf in delivering the service and is not engaged by the Client directly. | CarePro is a technology platform that connects clients seeking care services with independent caregivers. Caregivers are independent contractors and market participants … CarePro does not employ caregivers and is not a party to the care relationship … facilitates the matching, payment processing, and contract management only. | ☐ |
| 6 | Section 11.1 (bullet 1) | `CaregiverVettingClaim` | The Caregiver has been verified through CarePro's onboarding and vetting process. | … onboarding and background check process. | ☐ |
| 7 | Section 13, bullet 1 | `LiabilityLine1` | CarePro is responsible to the Client for the delivery of the agreed care package, subject to the terms of this agreement. | CarePro operates as a marketplace platform and is not liable for the actions or omissions of either party. | ☐ |
| 8 | Section 13, bullet 2 | `LiabilityLine2` | The Caregiver delivers the care on CarePro's behalf and is expected to provide it to a professional standard. | The Caregiver operates as an independent contractor and is solely responsible for the quality of care provided. | ☐ |
| 9 | Section 14 | `AcknowledgmentText` | The Client and CarePro acknowledge they have read, understood, and agree to all terms of this Care Service Agreement. The Caregiver acknowledges the terms that apply to delivering the service. | Both parties acknowledge they have read, understood, and agree to all terms of this Care Service Agreement. | ☐ |
| 10 | Cancellation acknowledgement (contract, PDF, email, client view) | `CancellationAcknowledgement(hours, sentAt)` | You have {hours} hours from {contract sent date/time UTC} to cancel this order. If you do not cancel within this period, you confirm that you have read and agree to all the terms of this agreement. | *(new)* | ☐ |
| 10b | Cancellation window closing line (contract HTML/PDF, client email, client view) — added under row 10; row 10's sentence is unchanged | `CancellationClosesLine(windowEndsAt)` | The cancellation period closes on {window end, Africa/Lagos}. | *(new)* | ☐ |
| 11 | Client "I agree" checkbox label (frontend) | `PackageRequestDetail.jsx` | I have read and agree to the terms of this agreement. | *(new)* | ☐ |
| 12 | Client cancel dialog / success text (frontend) | `PackageRequestDetail.jsx` | Your order will be cancelled. Our team will contact you about any refund due. | *(new)* | ☐ |

## Time zone

All contract dates (sent, window end, agreed, cancelled, contract date and signature dates) are displayed in
Africa/Lagos and labelled **WAT** (e.g. "Friday, 9 October 2026 at 8:51 AM WAT"). Storage remains UTC. Formatting lives in
`Application/Content/LagosTime.cs` (backend) and `src/main-app/utils/lagosTime.js` (frontend).

## Questions the lawyer should answer

- **Section 13** — the old exclusion of liability ("not liable for … either party", "solely responsible") was removed
  because it contradicted the agency model. **No replacement limitation of liability has been written.** Does CarePro want
  a cap/exclusion, and in what form?
- **Termination, safety and confidentiality clauses** still say "either party" / "both parties" / "neither party" in a
  client-and-caregiver sense (e.g. 11.2 confidentiality between the parties, 9–10 cancellation/termination). They were not
  changed. Do they need to be re-expressed as CarePro ↔ Client?
- **Caregiver acknowledgement** — the caregiver is no longer a party but still signs. Is a separate caregiver
  engagement/terms document needed?
- **Agreement by non-cancellation** (row 10) and the optional explicit tick (row 11): is silence after the window
  enforceable on its own, and is the wording adequate for consumer-protection rules in Nigeria?
- **Background checks** — row 6 deliberately makes no "background check" claim. Restore only once police clearance is
  recorded per caregiver in the system (change `CaregiverVettingClaim` only).
- **Refund language** (row 12) deliberately promises no refund.

## Not changed on purpose (owner decision)

Terms & Conditions, Privacy Policy and `MarketingPage.jsx:250` (background-check claim) are untouched.

---

# Police clearance — PROPOSED Privacy Policy wording (not applied)

**Status: DRAFT, LOCAL ONLY. The public Privacy Policy and Terms pages have NOT been edited (owner decision). Nothing below is live until a lawyer approves it and the owner publishes it.**

Police clearance documents are personal data. They are collected as an uploaded document on the caregiver Vetting page and
reviewed by CarePro Operations staff.

## Proposed sentence for the Privacy Policy

> If you work with CarePro as a caregiver, we ask you to upload a police clearance certificate so that our Operations team can
> review it before you are assigned to clients. The document is stored in private storage that is not publicly accessible. Only you
> and authorised CarePro Operations staff can open it, and every time a staff member opens it, we record who and when. We keep the
> document, and the record of our review, for as long as your caregiver account is active. If you delete your account, we
> permanently delete the document and the review record 30 days after your request, together with the rest of your personal
> data. We keep a limited audit entry (which staff member took an action, on which account, and when) for accountability.

## What the system actually does today (so the wording above stays true)

| Item | Behaviour |
|---|---|
| Storage | Cloudinary **authenticated** (non-public) raw assets. No public URL exists; no storage URL is returned by any API or written to logs. Files are read only through backend routes that check the requester. |
| Who can open a file | The owning caregiver; Operations/Policy admins (SuperAdmin, or Admin in HR / ComplianceAndLegal / CareLeads). Every other admin department and every other caregiver gets 403. |
| Staff views | Each staff view writes an `AdminAuditLog` row (admin id and email from the token, clearance id, caregiver id, file index, time) **before** the file is served. The original file name is deliberately not logged. |
| Retention while the account is active | **No automatic expiry or deletion.** An approved clearance stays approved. Older submissions that were replaced by a resubmission are kept as history (the caregiver cannot delete them). |
| Account deletion | After the existing 30-day grace period, `UserHardDeleteProcessor` deletes every clearance record for the account (including superseded ones) **and** deletes each file from storage. If a file cannot be deleted the account's processing fails loudly (ERROR log naming the opaque storage ids) and is retried on the next daily cycle. |
| Kept after deletion | `AdminAuditLog` rows (admin id/email, caregiver id, action, free-text reason typed by staff, file index, time). Staff reasons are free text and could in principle name the person — reviewers should avoid including names in reasons. |
| Not done | No antivirus scan. PDFs containing scripts/attachments are rejected, and files must match their declared type by magic bytes. No automatic retention period has been decided. |

## Questions for the lawyer

- Is "for as long as your caregiver account is active" an acceptable retention statement, or is a fixed period required (and if so, which)?
- Is a limited audit trail after account deletion acceptable, and how should the policy describe it?
- Does collecting police clearance need an explicit consent step or a stated lawful basis in the Privacy Policy?
- Should "background-checked" wording on the public pages be replaced with something like "police clearance reviewed"? (A clearance document is not a live criminal-record search.)

## Public wording to review when the step goes live (NOT changed in this pass)

- `Application/Content/ContractWording.cs` — `CaregiverVettingClaim` (and its comment block; row 6 above)
- `src/pages/MarketingPage.jsx` lines 84 ("Rigorously vetted"), 145 ("background-checked & verified"), 250 ("background checks")
- `src/components/LandingPage/FAQ.jsx:36` ("thorough background checks")
- `src/pages/TermsAndConditions.jsx` lines 53, 95 and `src/pages/PrivacyPolicy.jsx` lines 57, 92 (background-check information / providers)
