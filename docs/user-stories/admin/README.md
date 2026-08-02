# Admin user stories

Stories for the **admin** persona (per [docs/personas.md](../../personas.md)): 2 people sharing one `admin` role; daily checks; weekly payout batch run.

Every admin write is automatically audited (ADR 0014) via `AdminAuditPipelineBehavior`.

---

## US-admin-0001 — Log in to admin

### Narrative
As an admin, I want to log in to the admin host with email + password (no OAuth).

### Roles in play
- **AuthService** — issues JWT with `aud=admin`; admin can access all hosts.

### Acceptance criteria
- **AC-1** Given the admin enters correct credentials on `https://admin.makables.cz`, when login succeeds, then the JWT has `aud=admin`.
- **AC-2** Given a non-admin user tries to log in to the admin host, then `auth.forbidden` (their JWT would have `aud=customer | maker`, not `admin`).
- **AC-3** Given a Google OAuth callback hits the admin audience, then it's rejected with `auth.oauthNotAllowedForAdmin`.

### Related
- ADRs: 0005, 0012
- Roles: `auth-service`

---

## US-admin-0002 — Admin dashboard overview

### Narrative
As an admin, I want a single-page dashboard summarizing platform health and recent activity so I know where to focus.

### Roles in play
- (Reads multiple aggregates for stats)
- **Outbox** — surface outbox lag + stalled count
- **AdminAuditLogEntry** — recent activity feed (last 50)

### Acceptance criteria
- **AC-1** Given the admin lands on `/dashboard/admin`, when the page loads, then they see:
  - Daily counters: new orders, new makers, new disputes, new reviews
  - Outbox: current lag (seconds), stalled count, recent failures
  - Pending: orders in `PendingPayment` >24h (auto-cancel candidates), unverified makers, open disputes
  - Recent activity: last 50 admin audit entries with who/what/when
- **AC-2** Given outbox is stalled (>10 unrecoverable events) or lag > 5 min, when the page loads, then a red banner surfaces it.

### Related
- ADRs: 0014, 0020, 0023
- Roles: `outbox`, `admin-audit-log-entry`, `order`, `maker`

---

## US-admin-0003 — Verify a maker

### Narrative
As an admin, I want to mark a maker as verified after a review so they get the "Ověřeno" badge.

### Roles in play
- **Maker** — `VerifyMaker.Command` (audited).

### Acceptance criteria
- **AC-1** Given the maker exists, when the admin clicks "Ověřit" on the maker detail page, then `IsVerified=true` and an audit entry is written with `before_json` and `after_json`. Maker is unaffected operationally — they can already accept orders; this is purely a customer-visible badge.
- **AC-2** Given the maker is already verified, when the admin clicks, then `maker.alreadyVerified` (no-op).
- **AC-3** Given the audit entry is written, when the maker views their profile, then no notification — the badge appears silently.

### Out of scope
- Verification criteria / documents (admin verifies based on offline knowledge at MVP).

### Related
- ADRs: 0014
- Roles: `maker`, `admin-audit-log-entry`

---

## US-admin-0004 — Deactivate a maker

### Narrative
As an admin, I want to deactivate a maker (e.g. for policy violations) so their products disappear from the public catalog.

### Roles in play
- **Maker** — `DeactivateMaker.Command` (audited).

### Acceptance criteria
- **AC-1** Given the maker is active, when admin confirms deactivation with a notes field, then `IsActive=false`, `DeactivatedBy/On` set, all products soft-disappear from public catalog. In-flight orders continue (maker can still ship; customers can still see/track/message).
- **AC-2** Given the maker is deactivated, when they log in, then they see a banner explaining their account is suspended with a contact-admin link.
- **AC-3** Given the maker is deactivated, when a customer tries to place a new order on one of their products, then `maker.notActive`.

### Related
- ADRs: 0014
- Roles: `maker`

---

## US-admin-0005 — Re-fetch ARES data for a maker

### Narrative
As an admin, I want to refresh a maker's ARES snapshot when their company data changed (rare).

### Roles in play
- **Maker** — `RefreshMakerFromAres.Command` (audited).
- **CompanyRegistry** (ARES).

### Acceptance criteria
- **AC-1** Given the admin clicks "Refresh ARES" on a maker detail page, when ARES returns data, then `CompanyName`, `LegalForm`, `DIČ`, `RegisteredAddress` are updated on the Maker. An audit entry captures before/after.
- **AC-2** Given ARES is unreachable, when the action is attempted, then `Transient` error returned; admin can retry.
- **AC-3** Given updated data, when subsequent invoices are issued, then they use the new data. Past invoices remain unchanged.

### Related
- ADRs: 0014, 0018
- Roles: `maker`, `company-registry`

---

## US-admin-0006 — Edit country configuration

### Narrative
As an admin, I want to edit per-country settings (VAT rates, default providers, invoicing mode) without a deploy.

### Roles in play
- **CountryConfiguration** — `UpdateCountryConfiguration.Command` (audited).

### Acceptance criteria
- **AC-1** Given the admin visits `/dashboard/admin/countries/CZ`, when they update VAT rates (`StandardVatRateBp`, `ReducedVatRateBp`), default provider codes, invoicing mode, etc., and save, then the changes apply atomically with an audit entry.
- **AC-2** Given the admin changes a `Default*Provider` to a code not registered as a keyed service, when saved, then `country.providerNotRegistered` and the change is rejected.
- **AC-3** Given the admin changes the default payment provider, when saving, then a confirmation modal requires retyping the new provider code (high-stakes change).
- **AC-4** Given a config change is saved, when the next request reads the configuration, then it sees the new values (no cache delay beyond per-request).

### Related
- ADRs: 0004, 0014
- Roles: `country-configuration`

---

## US-admin-0007 — Run weekly payout batch

### Narrative
As an admin, I want to run the weekly payout batch and export the CSV for bulk bank transfer.

### Roles in play
- **PayoutBatch** — `CreatePayoutBatch.Command` (audited).
- **Order** — orders in `Delivered` state without a batch get included.
- **Invoice** — one fee invoice per maker per batch.
- **BlobStorage** — stores the CSV.

### Acceptance criteria
- **AC-1** Given the timer triggers on Monday 02:00 UTC OR the admin clicks "Spustit výplaty" with a confirmation, when the command runs, then:
  - All `Delivered` orders without `PayoutBatchId` are claimed for the batch
  - One fee invoice per maker is generated (PDF rendered, stored in blob, attached to email outbox)
  - A CSV in Czech-bank format is generated and stored in blob
  - The batch transitions `Pending` → `Processing`
  - Audit entry records the run
- **AC-2** Given the admin downloads the CSV, when they import it to their bank and execute, then they return to admin UI and click "Označit jako zaplaceno". Batch transitions `Processing` → `Completed`; included orders transition `Delivered` → `Completed`; makers receive `payout-sent` outbox emails.
- **AC-3** Given no eligible orders exist (no `Delivered` orders without batch), when run is triggered, then the command returns `payoutBatch.empty` and no batch is created. Audit entry still records the attempt.
- **AC-4** Given the batch is `Processing`, when admin re-runs the weekly batch, then the existing one is shown rather than a new one created.

### Related
- ADRs: 0009, 0014, 0020
- Roles: `payout-batch`, `order`, `invoice`, `blob-storage`

---

## US-admin-0008 — Refund an order

### Narrative
As an admin, I want to refund an order (full or partial) when a customer dispute warrants it.

### Roles in play
- **Order** — `RefundOrder.Command` (audited).
- **PaymentProvider** — `RefundAsync`.
- **Invoice** — credit note generated (post-MVP — at MVP, refund recorded in the order audit + email; full credit-note invoice in v1.1).

### Acceptance criteria
- **AC-1** Given the order is `Paid` or later AND has a `PaymentProviderRef`, when admin submits the refund command with an amount (full or partial), a reason, and a notes field, then `PaymentProvider.RefundAsync` is called. On success: order transitions to `Refunded` (full) or stays in current state with a `refunded_amount_minor` annotation (partial — post-MVP detail). Customer receives an `order-refunded` outbox email.
- **AC-2** Given the refund is for an order in `Completed` and already paid out to maker, when admin attempts the refund, then a warning surfaces: the refund will create a negative balance on the maker's next payout. Admin must acknowledge. Audit entry includes the acknowledgement.
- **AC-3** Given Comgate returns `Permanent` error (e.g. refund window expired), when refund fails, then admin sees the error and the order is unchanged.

### Related
- ADRs: 0014, 0016
- Roles: `order`, `payment-provider`, `invoice`

---

## US-admin-0009 — View all orders + filter

### Narrative
As an admin, I want to view all orders across all makers + customers with filters.

### Roles in play
- **Order** — `IOrderRepository.Unscoped()`.

### Acceptance criteria
- **AC-1** Given the admin visits `/dashboard/admin/orders`, when the page loads, then a paginated list of all orders sorted by `CreatedAt DESC` (filters: state, country, date range, maker, customer email).
- **AC-2** Given the admin clicks an order, when the detail page loads, then they see everything the maker and customer see plus the audit-trail tab.

### Related
- ADRs: 0013, 0014
- Roles: `order`, `admin-audit-log-entry`

---

## US-admin-0010 — Change order state manually

### Narrative
As an admin, I want to manually transition an order's state (e.g. force-cancel a stuck order) for exception handling.

### Roles in play
- **Order** — `ChangeOrderStateManually.Command` (audited).

### Acceptance criteria
- **AC-1** Given the order's current state, when admin selects a valid target state from a dropdown with a reason field, then the transition happens with an audit entry. Available targets respect the state machine (no `Completed` → `PendingPayment`).
- **AC-2** Given the target state requires a side effect (e.g. refund), when admin selects it without running the proper command first, then the system rejects with a hint to use `RefundOrder.Command` instead.
- **AC-3** Given the audit entry captures the transition, when the maker or customer views the order, then the timeline shows the admin transition with a "manual admin action" tag.

### Out of scope
- Bulk state changes.

### Related
- ADRs: 0014
- Roles: `order`

---

## US-admin-0011 — Open and resolve a dispute

### Narrative
As an admin, I want to receive customer/maker dispute reports, review evidence, and resolve them.

### Roles in play
- **Order** — `OpenDispute.Command` (customer or maker); `ResolveDispute.Command` (admin, audited).
- **OrderMessage** — preserved as evidence.

### Acceptance criteria
- **AC-1** Given a customer or maker opens a dispute on an order, when they submit the form with a category and description, then the order transitions to `Disputed` (sub-state of whatever state it was in — escrow holds). Admin gets `notification.admin` outbox event.
- **AC-2** Given the admin reviews the dispute, when they decide (refund / release-to-maker / partial / mark-as-fraud), then the appropriate command runs (RefundOrder, ResumePayout, etc.), the dispute is marked resolved with notes, and both parties get notified.
- **AC-3** Given a dispute is open, when the auto-deliver cron runs against the order, then auto-deliver is skipped for disputed orders.

### Out of scope
- Full mediation UI (post-MVP). At MVP, the dispute creates an admin task; resolution uses existing commands (refund, manual state change, etc.).

### Related
- ADRs: 0014
- Roles: `order`, `order-message`

---

## US-admin-0012 — View invoices list

### Narrative
As an admin, I want to see all invoices the platform has issued.

### Roles in play
- **Invoice** — `IInvoiceRepository.Unscoped()`.

### Acceptance criteria
- **AC-1** Given the admin visits `/dashboard/admin/faktury`, when the page loads, then a paginated list of all invoices sorted by `IssueDate DESC` (filters: type, country, date range, recipient).
- **AC-2** Given the admin clicks "Stáhnout fakturu", when the request is authorized, then the PDF streams from blob.
- **AC-3** Given a gap exists in invoice numbering for some reason (it shouldn't, but if), when the admin views the list, then a "Gap detected" warning surfaces. (Should never fire — `InvoiceNumbering` is gap-free by design.)

### Related
- ADRs: 0009
- Roles: `invoice`

---

## US-admin-0013 — Manage categories

### Narrative
As an admin, I want to add, rename, or hide categories.

### Roles in play
- **Category** — `CreateCategory.Command`, `UpdateCategory.Command`, `DeactivateCategory.Command` (all audited).

### Acceptance criteria
- **AC-1** Given the admin adds a category, when saved, then it appears in the public filter list and is selectable by makers in product forms.
- **AC-2** Given the admin renames a category, when saved, then it's renamed everywhere; existing products keep their FK.
- **AC-3** Given the admin deactivates a category, when saved, then it no longer appears in new-product forms but existing products remain in it. Public catalog hides the filter chip.

### Related
- ADRs: 0014
- Roles: `category`

---

## US-admin-0014 — Force-retry / acknowledge stalled outbox events

### Narrative
As an admin, I want to handle outbox events stuck in `Permanent` or `Configuration` states.

### Roles in play
- **Outbox** — `RetryOutboxEvent.Command` and `AcknowledgeOutboxEvent.Command` (audited).

### Acceptance criteria
- **AC-1** Given a stalled outbox event, when admin clicks "Retry now", then `next_retry_at = now()`, `retry_count` increments. `ProcessOutbox` picks it up on the next sweep.
- **AC-2** Given a stalled event is unsolvable (e.g. invalid email address), when admin clicks "Acknowledge", then `processed_at = now()` with a marker that says "manually acknowledged". The event is hidden from the stalled count.
- **AC-3** Both actions write an audit entry.

### Related
- ADRs: 0014, 0020
- Roles: `outbox`

---

## US-admin-0015 — View admin audit log

### Narrative
As an admin, I want to see what I and my fellow admin have been doing.

### Roles in play
- **AdminAuditLogEntry** (read)

### Acceptance criteria
- **AC-1** Given the admin visits `/dashboard/admin/audit`, when the page loads, then a paginated list of audit entries sorted by `created_at DESC` (filters: admin user, target entity, action code, date range).
- **AC-2** Given the admin clicks an entry, when the detail page loads, then they see `before_json` and `after_json` rendered as a side-by-side diff with sensitive fields redacted.

### Related
- ADRs: 0014
- Roles: `admin-audit-log-entry`

---

## US-admin-0016 — GDPR delete a user

### Narrative
As an admin responding to a GDPR right-to-erasure request, I want to permanently delete a user while preserving their orders in anonymized form.

### Roles in play
- **User** — `DeleteUserPermanently.Command` (audited, the only place hard-delete runs).
- **Order**, **Maker**, **Review** — anonymized (PII replaced with placeholders; FK preserved).

### Acceptance criteria
- **AC-1** Given the admin runs the command with a user id and reason, when it executes, then:
  - The user's row is hard-deleted.
  - Their refresh tokens are hard-deleted.
  - Their orders' `CustomerName/Email/Phone` are replaced with `Anonymized` placeholders.
  - If maker: the `Maker` row's PII fields are anonymized; `IČO`, `BankAccount` are retained for tax record purposes but flagged as `IsRetainedForLegal=true`.
  - Reviews from this user are anonymized (`Author = "Anonymized"`).
  - An audit log entry records the deletion (including `notes` and `reason`).
- **AC-2** Given the admin attempts to delete a user with in-flight orders (state `PendingPayment` / `Paid` / `Accepted` / `Shipped`), then the command is rejected with `user.cannotDeleteWithInFlightOrders` — admin must resolve those first.

### Related
- ADRs: 0013, 0014
- Roles: `user`, `order`, `maker`, `review`, `admin-audit-log-entry`

---

## US-admin-0017 — Log out

### Same shape as US-customer-0020.

---

## US-admin-0018 — Set a maker's loyalty fee-rate override

### Narrative
As an admin, I want to grant an individual maker a reduced commission rate (loyalty provize, 7% → 3,5%) so that makers who have cooperated with the platform for a while pay less, without touching the country-wide default rate.

### Roles in play
- **Maker** — extended with a nullable `FeeRateOverrideBp`. `SetMakerFeeOverride.Command` (audited) sets or clears it.
- **CountryConfiguration** (read) — `PlatformFeeRateBp` remains the fallback when no override is set.
- **OrderPricing** (read) — the platform-fee line in `OrderPricing.Compute` must read `maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp` instead of the country rate unconditionally; the resolved rate is still snapshotted onto the order at order-creation time, so historical orders are unaffected by a later override change.
- **Invoice** (read) — the fee invoice (JVM YORE → maker) generated by the weekly payout batch derives its commission line from the orders' already-snapshotted `platformFee`, so the effective (possibly overridden) rate is what shows on the fee invoice without any invoice-side change.
- **AdminAuditLogEntry** — before/after JSONB captures the override change.

### Acceptance criteria
- **AC-1** Given a maker with no override set, when the admin opens the maker detail page and sets a fee-rate override of 350 bp (3,5%) with a reason, then `Maker.FeeRateOverrideBp = 350` is persisted and an audit entry captures `before_json` (`null`) / `after_json` (`350`).
- **AC-2** Given a maker with an override already set, when a new order is priced for that maker's product, then `OrderPricing.Compute` uses the override rate (not `CountryConfiguration.PlatformFeeRateBp`) to compute `platformFee`, and the resolved rate is snapshotted on the order the same way the country default is today.
- **AC-3** Given a maker with no override, when an order is priced, then the platform fee uses `CountryConfiguration.PlatformFeeRateBp` exactly as it does today — behavior for makers without an override is unchanged.
- **AC-4** Given an admin clears a previously-set override (sets it back to "use default"), when saved, then `FeeRateOverrideBp = null` and subsequent orders for that maker revert to the country default. An audit entry captures the clear.
- **AC-5** Given the admin submits an override value outside a sane commission range (e.g. negative, or above the country's `PlatformFeeRateBp`), when saved, then the command is rejected with a validation error — an override must reduce the maker's commission, never raise it above the platform default. (BA default; see Alternatives Considered.)
- **AC-6** Given a payout batch runs for a maker with an active override, when the fee invoice for that batch is generated, then the invoice's commission total reflects the sum of the per-order `platformFee` snapshots (which already used the override rate) — no separate invoice-side override logic is needed.
- **AC-7** Given the override-setting endpoint is called without a resolvable admin session, then `401 auth.required` and nothing is persisted (fail-closed, per the `RefundOrder` / `UpdateCountryConfiguration` precedent).

### Out of scope
- Automatic/criteria-based award of the loyalty rate ("after longer cooperation" — months/orders/revenue thresholds). Per dopady §5.2, this is explicitly unresolved and admin-manual is the locked MVP fallback. A future automation ticket is blocked on that answer, not this one.
- More than two commission tiers (base vs. one override value) — the schema is a single nullable override, not a tiered ladder.
- Maker self-service visibility into *why* they got the rate (no "you qualified because X" messaging at MVP — the admin's `Reason` field is audit-log-only, not customer/maker-facing).
- Bulk override (CSV import / apply-to-many) — one maker at a time via the admin detail page.

### Alternatives considered
- **Option A — Store the override as a percentage float instead of basis points.** *Rejected* — every other rate in the platform (`PlatformFeeRateBp`, VAT rates) is basis points; a float would be the only non-bp rate field and would reintroduce floating-point rounding risk that the codebase has otherwise eliminated (ADR 0003).
- **Option B — Let the override raise the rate above the country default too (a general per-maker rate field, not a "loyalty discount").** *Rejected for MVP* — the business decision (dopady §2.2) is specifically a *discount* for loyalty; allowing an admin to silently raise a maker's fee above the advertised 7% risks a maker dispute/trust issue with no corresponding business need identified. AC-5 locks the override to `≤ CountryConfiguration.PlatformFeeRateBp`. If a future need for punitive/negotiated *higher* rates emerges, that is a new story on top of this schema (the nullable column doesn't preclude it).

### Related
- ADRs: 0004, 0014
- Ticket: T-0140
- Roles: `maker`, `country-configuration`, `order-pricing`, `invoice`, `admin-audit-log-entry`

---

## US-admin-0019 — Review maker-proposed categories

### Narrative
As an admin, I want a queue of categories makers proposed, so I can approve the good ones (opening them to every maker), fold the near-duplicates into what already exists, and reject the rest — keeping the public taxonomy coherent without blocking makers from listing their work.

### Roles in play
- **Category** — extended with `Status` (`Approved` / `Pending` / `Rejected`), `ProposedByMakerId`, `ReviewNote`, `MergedIntoCategoryId`. Three audited transitions: approve, reject, merge.
- **Product** — products in a pending category are withheld from every public surface; approval publishes them with no write to `products`, merge reassigns their `CategoryId`.
- **AdminAuditLogEntry** — before/after JSONB on each review action (`category.proposal.approve|reject|merge`).
- **Outbox** — `category.proposal.submitted.adminEmail` in, `category.proposal.reviewed.makerEmail` out.

### Acceptance criteria
- **AC-1** Given makers have proposed categories, when the admin opens the categories page, then a "Návrhy kategorií" queue lists each proposal with the proposed name, the proposing maker, how many products are waiting on it, and when it was submitted.
- **AC-2** Given a pending proposal, when the admin approves it, then `Status = Approved`, every waiting product becomes publicly visible immediately, and the category appears in the public filter list and in every maker's product-form picker.
- **AC-3** Given the proposed name or slug needs cleaning up ("resin tisk" → "Pryskyřicový tisk"), when the admin edits name, slug, icon, description and sort order as part of approving, then all five are persisted. The slug is editable **only** here — a pending slug has never been public, so no external link breaks (contrast US-admin-0013 AC-2, where rename freezes the slug).
- **AC-4** Given the corrected slug collides with an existing active category, when the admin approves, then it is refused with `category.slugAlreadyExists` and nothing is persisted.
- **AC-5** Given a proposal duplicates an existing category, when the admin merges it into that category, then the waiting products are reassigned to the target and become publicly visible, the proposal is marked rejected and deactivated with `MergedIntoCategoryId` recorded, and the maker is emailed naming the target category.
- **AC-6** Given a merge target that is missing, not approved, inactive, or the proposal itself, when submitted, then `category.proposal.mergeTargetInvalid` and nothing is persisted.
- **AC-7** Given a proposal the admin does not want, when they reject it with a required reason, then the category is marked rejected and deactivated, the waiting products stay hidden, and the maker sees the reason and can re-point their product to an existing category.
- **AC-8** Given approve / reject / merge is called on a category that is not `Pending` (already reviewed, or an ordinary admin-created category), then `category.proposal.notPending` and nothing is persisted.
- **AC-9** Given any review action succeeds, then an `AdminAuditLogEntry` captures before/after JSONB under the matching action code.
- **AC-10** Given no resolvable admin session, then `401 auth.required` and nothing is persisted.
- **AC-11** Given pending proposals exist, when the admin loads the dashboard, then a count tile shows the pending total and links to the queue — the same shape as the stalled-outbox and processing-payouts tiles.
- **AC-12** Given the existing admin category list, then it now shows each row's status so approved, pending and rejected rows are distinguishable at a glance.

### Out of scope
- Auto-approval of proposals from trusted makers, and fuzzy duplicate detection beyond exact slug match — every proposal is reviewed by a human at MVP.
- Bulk approve/reject.
- Re-categorising already-published products outside the merge path.
- Category hierarchy.

### Alternatives considered
- **Option A — Approve/reject only, no merge.** *Rejected* — makers will propose "Resin", "Pryskyřice" and "3D resin" for the same thing. Without merge the admin either accepts a fragmented public taxonomy or rejects proposals and strands the makers' products.
- **Option B — Let proposals publish immediately and moderate after the fact.** *Rejected* — the proposed name becomes an indexable URL segment and a public filter chip; post-hoc moderation means unmoderated maker text is publicly live in the interim.

### Related
- ADRs: 0004, 0011, 0013, 0014, 0020
- Ticket: T-0163
- Roles: `category`, `product`, `admin-audit-log-entry`, `outbox`
