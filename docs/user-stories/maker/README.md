# Maker user stories

Stories for the **maker** persona (per [docs/personas.md](../../personas.md)): solo OSVČ or small workshop; Czech business with IČO; single user per maker at launch.

---

## US-maker-0001 — Register as a maker (with ARES lookup)

### Narrative
As someone with a Czech business, I want to register as a maker by entering my IČO and having the company data auto-fill, so I don't retype information.

### Roles in play
- **User** — created with `Role=maker`.
- **Maker** — created by `RegisterMaker.Command` after the user is created.
- **CompanyRegistry** (ARES) — provides company data; data is **snapshot** onto the Maker (legal: invoices use this snapshot).
- **AddressGeocoder** — non-blocking, populates lat/long on the registered seat.
- **AuthService** — issues tokens; queues confirmation email.

### Acceptance criteria
- **AC-1** Given a person on `/pro-makery`, when they click "Registrovat se" and submit email + password + IČO, then ARES is queried, the form returns to them pre-filled with `CompanyName`, `LegalForm`, `DIČ` (if any), and `RegisteredAddress`. The user reviews and confirms.
- **AC-2** Given the user confirms and submits the second form step (bio, phone, bank account, categories, personal pickup), when all validations pass, then a `User` (with `Role=maker`) and a `Maker` are created atomically. User is logged in; email confirmation email queued.
- **AC-3** Given the IČO is invalid format (not 8 digits + valid mod-11 checksum), when submitted, then `validation.icoFormat` is returned without hitting ARES.
- **AC-4** Given the IČO is not found in ARES, when looked up, then `company.notFound` is returned to the form.
- **AC-5** Given a Maker with the same IČO already exists, when registration is attempted, then `maker.icoAlreadyRegistered` is returned.
- **AC-6** Given ARES is unreachable AND no cache entry exists, when the lookup is attempted, then a `Transient` error is returned with a "try again in a moment" hint.
- **AC-7** Given ARES is unreachable AND a cache entry ≤ 7 days old exists, when the lookup is attempted, then the cached data is returned with a `stale=true` flag; the form notes "data from cache".
- **AC-8** Given the bank account is malformed (Czech format `123456789/0100`), when submitted, then `validation.bankAccountFormat`.
- **AC-9** Given registration succeeds, when the email is unconfirmed, then the maker's products are not yet visible in the public catalog. Once confirmed, the maker enters the catalog (no admin gate at MVP).

### Out of scope
- Multi-user maker accounts (Q-0001).
- Document upload during registration (ID verification, contracts) — post-MVP.

### Related
- ADRs: 0010, 0012, 0014, 0018
- Roles: `user`, `maker`, `company-registry`, `address-geocoder`, `auth-service`

---

## US-maker-0002 — Log in as a maker

### Narrative
As a maker, I want to log in (password or magic link) so I can access my dashboard.

### Roles in play
- **AuthService** — issues JWT with `aud=maker`.

### Acceptance criteria
- **AC-1** Given a maker has `Role=maker`, when they log in via password or magic link, then the JWT has `aud=maker` and they are redirected to `/dashboard/maker`.
- **AC-2** Given a maker's email is unconfirmed, when they log in, then they see a banner reminding them to confirm; they can still access the dashboard but their products are not in the public catalog until confirmed.

### Related
- ADRs: 0012
- Roles: `auth-service`, `user`

---

## US-maker-0003 — Update profile (bio, phone, pickup info, categories, bank account)

### Narrative
As a maker, I want to keep my profile current so customers see accurate information and payouts hit the right account.

### Roles in play
- **Maker** — `UpdateMakerProfile.Command` patches editable fields.

### Acceptance criteria
- **AC-1** Given the maker edits bio (≤500 chars), phone, pickup info, categories, or bank account, when they save, then the changes are persisted with `UpdatedBy/On`.
- **AC-2** Given the maker tries to change `IČO` / `CompanyName` / `RegisteredAddress`, when the form is rendered, then those fields are read-only with a "Contact admin to update" note. (Legal: ARES-snapshot fields shouldn't change silently.)
- **AC-3** Given the bank account fails Czech format validation, when submitted, then it's rejected with `validation.bankAccountFormat`.

### Out of scope
- Maker-self-service ARES re-fetch (admin-triggered only at MVP).

### Related
- Roles: `maker`

---

## US-maker-0004 — Create / edit / delete a product

### Narrative
As a maker, I want to add products with photos, description, price, and weight so customers can order them.

### Roles in play
- **Product** — CRUD via `CreateProduct.Command`, `UpdateProduct.Command`, `DeleteProduct.Command`.
- **BlobStorage** — receives image uploads via backend.
- **Category** (read).
- **Money** (used).

### Acceptance criteria
- **AC-1** Given the maker fills the product form (title, description, category, price, price_type, weight, up to 10 images), when they submit, then a `Product` is created with `IsActive=true` and images are stored under `cz/products/<productId>/<filename>` in `product-images`.
- **AC-2** Given an image upload exceeds 5 MB or has an unsupported MIME (allowed: jpeg, png, webp), when uploaded, then the API rejects with `file.invalid` and the form shows a Czech message.
- **AC-3** Given the maker edits a product, when they save, then changes are persisted. Existing orders are unaffected — they hold the pricing snapshot from order time.
- **AC-4** Given the maker deletes a product, when they confirm the modal, then `IsActive=false` (soft delete). Existing orders remain visible to all parties; the product is removed from the public catalog.

### Related
- ADRs: 0003, 0011
- Roles: `product`, `maker`, `blob-storage`, `money`

---

## US-maker-0005 — View incoming orders

### Narrative
As a maker, I want to see all orders for my workshop in one list so I know what to work on.

### Roles in play
- **Order** (read; scoped via `IOrderRepository.ForMaker(makerId)`)

### Acceptance criteria
- **AC-1** Given the maker visits `/dashboard/maker/objednavky`, when the page loads, then orders fulfillable by their maker are paginated (20 per page) sorted by `CreatedAt DESC`.
- **AC-2** Given filters (state, customer name, date range) are applied, when the list re-loads, then results match the filter combination.
- **AC-3** Given an order in `Paid` state requires attention, when the dashboard summary loads, then a "X nových objednávek čeká" badge surfaces the count.

### Related
- Roles: `order`

---

## US-maker-0006 — Accept an order

### Narrative
As a maker, I want to accept a paid order so the customer knows I'm working on it.

### Roles in play
- **Order** — `AcceptOrder.Command` transitions `Paid` → `Accepted`.

### Acceptance criteria
- **AC-1** Given the order is `Paid` and assigned to the maker, when they click "Přijmout objednávku", then it transitions to `Accepted` with `AcceptedAt`, and an outbox event notifies the customer.
- **AC-2** Given the order is in any state other than `Paid`, when accept is attempted, then `order.invalidTransition`.
- **AC-3** Given the maker hasn't accepted within 48 h of payment, when the auto-nudge job runs, then a reminder email + admin notification fires (post-MVP candidate; logged as a follow-up ticket).

### Related
- Roles: `order`

---

## US-maker-0007 — Mark an order as shipped (Zásilkovna)

### Narrative
As a maker with an accepted order shipping via Zásilkovna, I want to mark it shipped to trigger packet creation and customer notification.

### Roles in play
- **Order** — `ShipOrder.Command` transitions `Accepted` → `Shipped`.
- **ShippingCarrier** (Packeta) — `CreateShipmentAsync` produces `CarrierRef + TrackingUrl`.
- **BlobStorage** — label PDF cached after Packeta call.

### Acceptance criteria
- **AC-1** Given the order is `Accepted` and `ShippingMethod=Zasilkovna`, when the maker clicks "Označit jako odesláno", then a Packeta shipment is created, `Order.CarrierRef + TrackingUrl + ShippedAt + AutoDeliverAt` are set, state transitions to `Shipped`, customer is notified via outbox.
- **AC-2** Given Packeta returns a validation error (e.g. unknown pickup point), when the call fails with `Permanent`, then the maker sees the error and can correct (e.g. ask the customer for a new pickup point via messaging). Order stays `Accepted`.
- **AC-3** Given Packeta is transiently down, when the call fails with `Transient`, then the action is rejected with "Doprava dočasně nedostupná, zkuste znovu". The maker can retry.

### Related
- ADRs: 0017
- Roles: `order`, `shipping-carrier`, `blob-storage`

---

## US-maker-0008 — Mark an order as handed over (personal pickup)

### Narrative
As a maker with an accepted order on personal pickup, I want to mark it as handed over so the escrow timer starts.

### Roles in play
- **Order** — `ShipOrder.Command` with `source = "handover"` transitions `Accepted` → `Shipped` without calling Packeta.

### Acceptance criteria
- **AC-1** Given the order is `Accepted` and `ShippingMethod=PersonalPickup`, when the maker clicks "Předáno zákazníkovi", then `ShippedAt = now()`, `AutoDeliverAt = now() + 7 days`, state = `Shipped`. No Packeta call.
- **AC-2** Given personal pickup, when the order page loads, then "Stáhnout štítek" is not shown (no label exists).

### Related
- Roles: `order`

---

## US-maker-0009 — Download a shipping label

### Narrative
As a maker who has shipped an order via Zásilkovna, I want to download the label PDF to print and attach to the package.

### Roles in play
- **Order** (read; ownership check)
- **ShippingCarrier** — `GetLabelPdfAsync` if not cached
- **BlobStorage** — cache hit serves directly

### Acceptance criteria
- **AC-1** Given the maker owns the order and it is `Shipped` via Zásilkovna, when they click "Stáhnout štítek", then a PDF streams from `/api/v1/maker/files/orders/<orderId>/label`.
- **AC-2** Given the label was already cached, when requested, then it streams from blob storage (no Packeta call).
- **AC-3** Given the label was not cached, when requested, then Packeta is called, the PDF is cached in blob storage, then streamed. Subsequent requests hit cache.
- **AC-4** Given a different maker's order id is requested, then 404.

### Related
- ADRs: 0011, 0017
- Roles: `order`, `shipping-carrier`, `blob-storage`

---

## US-maker-0010 — View order detail + customer attachments

### Narrative
As a maker, I want to see the full details of an order including any STL/PDF files the customer uploaded so I can produce the right item.

### Roles in play
- **Order** (read)
- **BlobStorage** (read; per-attachment access via `/api/v1/maker/files/orders/<orderId>/attachments/<filename>`)
- **OrderMessage** (read)

### Acceptance criteria
- **AC-1** Given the maker owns the order, when they visit `/dashboard/maker/objednavka/<id>`, then they see customer name + email + phone, shipping method + pickup point or address, all attachments (with download links), notes, the full timeline, and the action buttons appropriate to the current state.
- **AC-2** Given attachments include STL/3MF/OBJ, when the maker downloads them, then they stream through the backend (no direct blob URL).

> **Annotation (2026-06-12, Q-0016 ruled — option a):** the "email" in AC-1's grant is satisfied at the **commercial-document level, not the DOM**. Maker dashboard DOM surfaces exclude customer email (T-0081/T-0082 compile-time GDPR lock; contact is mediated by the order message thread, US-maker-0011), while the invoice PDF (T-0088 download) legitimately embeds customer email as sanctioned commercial-document content between contracting parties. AC-1 is reconciled with both locks under this reading.

### Related
- ADRs: 0011
- Roles: `order`, `blob-storage`, `order-message`

---

## US-maker-0011 — Message the customer

### Narrative
As a maker, I want to message the customer about an order to clarify details or arrange the personal pickup time.

### Roles in play
- **OrderMessage** (write)
- **Order** (read; must be in `Paid` or later)
- **EmailProvider** (via outbox; digest debounced ≥5 min)

### Acceptance criteria
- **AC-1** Given the order is `Paid` or later, when the maker submits a message ≤2000 chars, then the message is persisted and a `notification.customer` outbox event is enqueued.
- **AC-2** Given messages have been sent in the last 5 min for the same order, when a new message is sent, then the outbox event is grouped/debounced so the customer doesn't get spammed (one digest email per 5-min window per order).
- **AC-3** Given the maker tries to message a `PendingPayment` order, then `order.notPayableYet`.

### Related
- Roles: `order-message`, `order`, `email-provider`

---

## US-maker-0012 — View payouts

### Narrative
As a maker, I want to see how much I've been paid and when, broken down by order.

### Roles in play
- **PayoutBatch** (read; filtered by maker)
- **Invoice** (read; type=Fee, the platform→maker invoices)
- **Order** (read; orders in each batch)

### Acceptance criteria
- **AC-1** Given the maker visits `/dashboard/maker/vyplaty`, when the page loads, then they see a list of payout batches affecting them (paginated), each row showing batch number, processed_at, total amount paid out to the maker, count of orders, and a link to the fee invoice PDF.
- **AC-2** Given a batch is in `Pending` or `Processing`, when shown, then the row has "připravujeme" badge and the total is preview (admin hasn't confirmed bank yet).
- **AC-3** Given the maker clicks a batch row, when expanded, then the per-order breakdown is shown: order number, product price, platform fee deducted, shipping price reimbursed (if any), net payout.

### Related
- ADRs: 0009
- Roles: `payout-batch`, `invoice`, `order`

---

## US-maker-0013 — Download fee invoice

### Narrative
As a maker, I want to download the platform's fee invoice for each payout batch so my accountant can record the platform fee as an expense.

### Roles in play
- **Invoice** (read; type=Fee)
- **BlobStorage** (read)

### Acceptance criteria
- **AC-1** Given the maker owns the invoice (i.e. recipient is their maker entity), when they click "Stáhnout fakturu", then the PDF streams from `/api/v1/maker/files/invoices/<invoiceId>`.
- **AC-2** Given another maker's invoice id is requested, then 404.

### Related
- ADRs: 0009, 0011
- Roles: `invoice`, `blob-storage`

---

## US-maker-0014 — Respond to a review

### Narrative
As a maker who received a review, I want to add a public reply to it.

### Roles in play
- **Review** — `RespondToReview.Command` sets `MakerReply`.

### Acceptance criteria
- **AC-1** Given the review targets the maker, when they submit a reply ≤500 chars, then the review's `MakerReply` is updated.
- **AC-2** Given the maker tries to reply to another maker's review, then 404.
- **AC-3** Given the maker has already replied, when they submit a new reply, then it overwrites (one reply per review at MVP).

### Out of scope
- Disputing or flagging a review (admin-handled via direct support; post-MVP UI).

### Related
- Roles: `review`, `maker`

---

## US-maker-0015 — Disable / enable personal pickup

### Narrative
As a maker, I want to turn personal pickup on or off so I can pause it when I'm on holiday.

### Roles in play
- **Maker** — `UpdateMakerProfile.Command` flips `PersonalPickup`.

### Acceptance criteria
- **AC-1** Given the maker toggles personal pickup off, when saved, then new orders no longer see the personal-pickup option. Existing orders with `ShippingMethod=PersonalPickup` are unaffected.
- **AC-2** Given personal pickup is on but no pickup address is set, when saved, then `validation.pickupAddressRequired`.

### Related
- Roles: `maker`

---

## US-maker-0016 — Log out

### Narrative
As a maker, I want to log out.

### Same shape as US-customer-0020.

---

## US-maker-0018 — Set a product's fulfillment type ("na zakázku" vs. "skladem")

### Narrative
As a maker, I want to mark each product as made-to-order or in-stock so the customer sees the correct legal notice about their right to withdraw before they pay.

### Roles in play
- **Product** — extended with `FulfillmentType` (`MadeToOrder | InStock`; default `MadeToOrder`). `CreateProduct.Command` / `UpdateProduct.Command` gain the new field.

### Acceptance criteria
- **AC-1** Given the maker fills the product form, when they submit, then they choose `FulfillmentType` from a two-option control (defaulting to "Na zakázku"); the product is created with that value.
- **AC-2** Given an existing product created before this ticket shipped, when the migration runs, then it defaults to `MadeToOrder` (the safer legal default — most maker catalog items today are custom production, per personas.md).
- **AC-3** Given the maker edits an existing product, when they change `FulfillmentType` from `MadeToOrder` to `InStock` (or back), then the change is persisted and takes effect for all *future* checkouts of that product; it does not retroactively alter any notice already shown for past orders (checkout copy is a point-in-time display concern, not a stored order field — see US-customer-0021).
- **AC-4** Given the product detail page renders, when the customer views it, then a badge shows "Na zakázku" or "Skladem" matching the stored value (US-customer-0009 detail page, US-customer-0021 checkout copy).

### Out of scope
- Per-category default (all products default to `MadeToOrder` regardless of category, even for categories like `cat-handmade` where "skladem" might be common — maker sets it explicitly).
- Any inventory/stock-count tracking for `InStock` products (the flag only toggles the legal notice; stock quantity management is out of scope per personas.md "Stock / inventory (out of scope at MVP)").

### Alternatives considered
- **Option A — Derive fulfillment type from `PriceType` (e.g. `OnRequest` ⇒ made-to-order, `Fixed`/`From` ⇒ in-stock) instead of a new independent field.** *Rejected* — `PriceType` describes *pricing* certainty (fixed price vs. quote-based), not *fulfillment* timing; a `Fixed`-priced 3D print is still made-to-order (it just has a known price upfront). Conflating the two would produce wrong legal notices for the platform's dominant use case (personas.md: "products are made-to-order" for most makers). A dedicated field keeps the two concerns independent.

### Related
- ADRs: 0003 (money/pricing — unaffected), none new
- Ticket: T-0144
- Roles: `product`, `maker`

---

## US-maker-0019 — Respond to a customer dispute within 7 days

### Narrative
As a maker, I want a clear deadline to respond when a customer opens a complaint, so unresolved issues don't stall indefinitely and I don't risk an automatic escalation to Makables.

### Roles in play
- **Dispute** (read; existing aggregate per [dispute.md](../../architecture/roles/dispute.md)) — a new response-timer concept is layered on top of the existing `Source=Customer` open disputes; the Dispute row itself is unchanged.
- **OrderMessage** — the maker's reply lands in the existing order-scoped thread (US-maker-0011); a reply after the dispute opened counts as "responded".
- **Outbox** — a new `dispute.autoEscalated.adminEmail` event fires if the maker doesn't reply in time.
- A new Function (mirrors the T-0077 `AutoDeliverOrdersFunction` shape) sweeps open, customer-sourced disputes daily.

### Acceptance criteria
- **AC-1** Given a customer-opened `Dispute` (`Source = Customer`, still unresolved), when 7 days pass since `Dispute.CreatedAt` with no `OrderMessage` from the maker on that order posted after the dispute opened, then the sweep enqueues an admin-notification outbox event flagging the dispute as maker-unresponsive; the dispute itself stays `Disputed` (admin still resolves it via the existing `ResolveDispute.Command` — the escalation only surfaces it more urgently, it doesn't auto-resolve anything).
- **AC-2** Given the maker posts a reply on the order thread within 7 days of the dispute opening, when the daily sweep runs, then no escalation fires for that dispute.
- **AC-3** Given the dispute is resolved by admin before day 7, when the sweep runs, then it's excluded (the sweep predicate matches `ResolvedAt IS NULL`, the same idiom as the auto-deliver / auto-cancel sweeps).
- **AC-4** Given a maker-opened or admin-opened dispute (`Source = Maker | Admin`), then the 7-day maker-response timer does not apply (it is specifically the customer's escalation-path SLA on the maker).

### Out of scope
- Any automatic sanction against the maker for missing the 7-day window (three-tier warning/suspend/deactivate sanctions are T-0148, explicitly separate and blocked on its own open question).
- A visible countdown/nudge to the maker before day 7 (T-0148's SLA-timer nudges are the broader pattern; this ticket ships only the auto-escalation email at day 7, not earlier reminders).

### Alternatives considered
- **Option A — Auto-resolve the dispute in the customer's favor (e.g. auto-refund) if the maker doesn't respond in 7 days.** *Rejected* — dopady §2.5 / Q7 only specifies a response-time SLA that triggers *escalation to admin*, not an automatic money-moving outcome. `Dispute.Resolve` always requires an admin decision (per dispute.md's resolution-outcome dispatch to `RefundOrder`/`Cancel`); auto-refunding on a timer would bypass that safeguard and risk incorrect refunds on legitimate maker delays (e.g. maker on holiday, genuinely investigating).

### Related
- ADRs: 0014, 0017, 0020
- Ticket: T-0145
- Roles: `dispute`, `order-message`, `outbox`

---

## US-maker-0017 — Track outbox events for my orders (audit trail)

### Narrative
As a maker, I want to see whether the customer was notified, when the invoice was generated, etc., so I have an audit trail.

### Roles in play
- **Outbox** (read; filtered to events where `aggregate_id` is in the maker's orders)

### Acceptance criteria
- **AC-1** Given an order belongs to the maker, when they expand "události" on the order detail page, then they see a chronological list of outbox events (`order.paid`, `email.send`, `invoice.generate`, etc.) with status (success / scheduled retry / stalled).
- **AC-2** Given an outbox event is stalled (`last_error_type IN (Permanent, Configuration)`), when shown, then the event row has an alert badge. The maker can't retry — only admin can.

### Out of scope
- Maker-initiated retry (admin only).

### Related
- ADRs: 0020
- Roles: `outbox`, `order`
