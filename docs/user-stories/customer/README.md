# Customer user stories

Stories for the **customer** persona (per [docs/personas.md](../../personas.md)): mixed B2C + B2B, CZ-only at launch, escrow trust model (no pre-purchase contact).

This file collects every customer-facing capability as a story with a Roles block (per [ADR 0015](../../adr/0015-responsibility-driven-design.md)) and acceptance criteria.

---

## US-customer-0001 — Register a customer account

### Narrative
As a customer, I want to register an account with my email so that I can place orders, track them, and communicate with makers.

### Roles in play
- **User** — created by `RegisterCustomer.Command`. Role = `customer`. `EmailConfirmedAt = null`. Sends a confirmation email via outbox.
- **AuthService** — orchestrates registration: validates email/password, hashes via Argon2id, issues access + refresh tokens, queues the confirmation email.

### Acceptance criteria
- **AC-1** Given a never-registered email, when the customer submits the registration form with valid email + password (≥10 chars), then a `User` is created with `Role=customer`, the password is Argon2id-hashed, and the customer is logged in (access token + refresh cookie set).
- **AC-2** Given the registration succeeds, when the system completes, then an `email.send` outbox event is enqueued for the `welcome-customer` template in `cs-CZ`. The email arrives within 1 minute under normal conditions.
- **AC-3** Given an email already exists, when the customer attempts to register with it, then the system responds with `auth.emailAlreadyExists` and no user is created. The error message is generic (no leakage about whether the email is registered).
- **AC-4** Given a password in the top-100 breached list, when the customer attempts to register, then the system responds with `auth.passwordTooCommon` and no user is created.
- **AC-5** Given the registration succeeds, when the customer attempts to place an order before confirming their email, then the order placement is blocked with `auth.emailNotConfirmed`.

### Out of scope
- B2B account fields (IČO/DIČ on the user) — those are optional on the order, not on the account.
- Multi-user customer accounts.
- Username-based login (email-only).

### Related
- ADRs: 0012 (auth)
- Roles: `user`, `auth-service`

---

## US-customer-0002 — Log in with email + password

### Narrative
As a customer, I want to log in with my email and password so that I can access my dashboard.

### Roles in play
- **AuthService** — verifies credentials, enforces lockout, issues tokens.
- **User** — read; `FailedLoginCount` incremented on failure.

### Acceptance criteria
- **AC-1** Given a registered, non-locked-out user, when they submit correct credentials, then they receive an access token (15 min) and a refresh cookie (30 days, HttpOnly Secure SameSite=Strict).
- **AC-2** Given a registered user, when they submit wrong credentials 5 consecutive times within 15 min, then the account is locked for 15 min. Subsequent attempts return `auth.locked` regardless of correctness.
- **AC-3** Given a non-registered email, when login is attempted, then the response is indistinguishable from a wrong-password response (same status code, generic error). No email enumeration leakage.
- **AC-4** Given a user logs in, when their access token expires, then the refresh endpoint exchanges the refresh cookie for a new access token and rotates the refresh cookie. Reuse of the old refresh token revokes the entire family.

### Out of scope
- MFA at launch.
- "Remember this device" longer-term refresh.

### Related
- ADRs: 0012
- Roles: `auth-service`, `user`

---

## US-customer-0003 — Log in via magic link

### Narrative
As a customer, I want to log in without remembering a password by entering my email and clicking a link sent to that email.

### Roles in play
- **AuthService** — issues and consumes magic-link tokens.
- **EmailProvider** (via outbox) — delivers the magic-link email.

### Acceptance criteria
- **AC-1** Given a customer enters their email and clicks "Send me a link", when the request succeeds, then an outbox event for `magic-link` template is enqueued. The email contains a single-use link valid for 15 min.
- **AC-2** Given the customer clicks the link, when the token is valid and unused, then they're logged in (access + refresh issued). The token is invalidated on first use.
- **AC-3** Given a customer enters an email that doesn't exist, when they submit the magic-link form, then the response is 200 with no leakage. No email is sent.
- **AC-4** Given a customer requests 3 magic links within 10 min, when they request a 4th, then the system returns `auth.rateLimited` and skips sending.

### Out of scope
- Magic-link login that also creates a customer on first use (the user must register first; magic link is a login method, not a sign-up shortcut).

### Related
- ADRs: 0012
- Roles: `auth-service`, `email-provider`

---

## US-customer-0004 — Log in via Google

### Narrative
As a customer, I want to log in with my Google account to avoid creating yet another password.

### Roles in play
- **AuthService** — orchestrates the OAuth flow.
- **GoogleOAuthClient** — exchanges code for profile.
- **User** — created on first Google login (with `EmailConfirmedAt = now()`) or linked to an existing email-matched user.

### Acceptance criteria
- **AC-1** Given a customer clicks "Continue with Google" on `/auth/login`, when they complete the Google consent flow, then they're returned to the platform and logged in.
- **AC-2** Given the Google profile email matches an existing `User`, when login completes, then `GoogleSub` is linked to that user and a confirmation email is sent (`google-account-linked` template).
- **AC-3** Given the Google profile email does not match an existing user, when login completes, then a new `User` is created with `Role=customer`, `EmailConfirmedAt=now()`, and the customer is logged in.
- **AC-4** Given a Google OAuth callback is attempted against an `admin` audience, when the audience parameter is `admin`, then login is rejected with `auth.oauthNotAllowedForAdmin`.

### Out of scope
- Other OAuth providers (Apple, Microsoft) — post-MVP.

### Related
- ADRs: 0012
- Roles: `auth-service`, `user`

---

## US-customer-0005 — Confirm email address

### Narrative
As a customer, I want to confirm my email by clicking the link sent to me so that I can place orders.

### Roles in play
- **AuthService** — consumes confirmation tokens.
- **User** — `EmailConfirmedAt` populated.

### Acceptance criteria
- **AC-1** Given a registered customer with unconfirmed email, when they click the confirmation link within 24 h of registration, then `EmailConfirmedAt` is set to `now()`.
- **AC-2** Given a customer with unconfirmed email tries to place an order, when they hit `POST /api/v1/customer/orders`, then the response is `auth.emailNotConfirmed` with status 403.
- **AC-3** Given a confirmation token is expired or already used, when the link is clicked, then the page offers a "Resend confirmation" CTA which queues a fresh outbox event.

### Out of scope
- Confirming via SMS / OTP.

### Related
- ADRs: 0012
- Roles: `auth-service`, `user`, `email-provider`

---

## US-customer-0006 — Reset forgotten password

### Narrative
As a customer who forgot my password, I want to reset it by clicking a link sent to my email.

### Roles in play
- **AuthService** — issues and consumes reset tokens; revokes all refresh tokens on completion.

### Acceptance criteria
- **AC-1** Given a customer enters their email on `/auth/reset`, when the request succeeds, then a `reset-password` outbox event is enqueued. Email contains a single-use link valid for 1 h. Response is 200 regardless of email existence (no enumeration).
- **AC-2** Given the customer clicks the link and submits a new valid password, when the request succeeds, then `PasswordHash` is updated and all of the user's refresh tokens are revoked.
- **AC-3** Given the new password is in the breached top-100, when submitted, then it's rejected with `auth.passwordTooCommon`.

### Related
- ADRs: 0012
- Roles: `auth-service`

---

## US-customer-0007 — Browse the catalog

### Narrative
As a customer, I want to browse the catalog by category, city, or rating so that I can find a maker for my project.

### Roles in play
- **Maker** (read) — list filtered by serviced country, active makers only.
- **Product** (read) — joined when a category filter is applied.
- **Category** (read) — for the filter chip list.

### Acceptance criteria
- **AC-1** Given the catalog page loads, when the customer has not chosen a filter, then up to 24 makers per page are shown, sorted by rating average descending, then by total orders descending.
- **AC-2** Given the customer selects a category, when the filter applies, then only makers offering that category are shown.
- **AC-3** Given the customer enters a city, when they submit the city filter, then only makers in that city are shown. The city filter accepts partial matches (e.g. "Praha" matches "Praha 2").
- **AC-4** Given the catalog has more than 24 makers in the filtered set, when the page is at page 1, then a "Načíst další" button or pagination control loads the next batch. Browser back returns to the previous page state including filters and page number (URL-state-driven).
- **AC-5** Given a maker is inactive (`IsActive=false`, `User.IsActive=false`, or unconfirmed email), when the catalog is queried, then they don't appear.

### Out of scope
- Map-based "makers near you" (post-MVP; coordinates already stored).
- Free-text search.
- Sort options beyond rating + total orders.

### Related
- Roles: `maker`, `product`, `category`

---

## US-customer-0008 — View a maker profile

### Narrative
As a customer, I want to see a maker's profile with their bio, products, reviews, and contact options.

### Roles in play
- **Maker** (read)
- **Product** (read; all active products of the maker)
- **Review** (read; aggregated rating + recent reviews)

### Acceptance criteria
- **AC-1** Given a maker has a published slug, when the customer visits `/katalog/<slug>`, then they see the maker's bio, photo (if any), categories offered, rating + count, total orders fulfilled, and a paginated list of their active products.
- **AC-2** Given the maker offers personal pickup, when the profile loads, then the pickup address and note are shown.
- **AC-3** Given a maker has reviews, when the profile loads, then the latest 5 reviews are shown with rating, comment excerpt, and maker reply if present. A "view all reviews" link loads more.
- **AC-4** Given a maker is inactive, when the customer visits the slug, then the page returns 404.

### Out of scope
- "Follow this maker" / favorites.
- Direct messaging from the profile (escrow model — only post-order).

### Related
- Roles: `maker`, `product`, `review`

---

## US-customer-0009 — View a product detail

### Narrative
As a customer, I want to see a product's full details, including photos, description, price, and the maker behind it.

### Roles in play
- **Product** (read)
- **Maker** (read; for the maker info card)
- **Money** (used: price formatting)

### Acceptance criteria
- **AC-1** Given an active product, when the customer visits `/produkt/<id>`, then they see title, all images, full description, formatted price (`579 Kč` for CZ), maker info card with link to maker profile.
- **AC-2** Given the product has `PriceType=fixed`, when the page loads, then an "Objednat" CTA is enabled.
- **AC-3** Given the product has `PriceType=from`, when the page loads, then the price label reads "Od X Kč" and the CTA proceeds to the order form (quantity influences price).
- **AC-4** Given the product has `PriceType=on_request`, when the page loads, then no order CTA is shown; instead a "Coming soon — custom quotes" placeholder appears (per Q-0002 default).
- **AC-5** Given the product is inactive or the maker is inactive, when visited, then the page returns 404.

### Related
- Roles: `product`, `maker`, `money`

---

## US-customer-0010 — Place an order with Zásilkovna delivery

### Narrative
As an authenticated, email-confirmed customer, I want to place an order for a product with Zásilkovna pickup-point delivery so that I receive what I bought.

### Roles in play
- **Order** — created by `CreateOrder.Command`. State `PendingPayment`.
- **OrderPricing** (asks: compute breakdown)
- **OrderNumbering** (asks: next number; `M-CZ-NNNN...`)
- **Product**, **Maker** (read)
- **ShippingCarrier** — for the widget config; not called server-side yet (server creates the shipment later, on maker "ship")
- **PaymentProvider** — `CreatePaymentAsync` returns the redirect URL
- **AddressGeocoder** — non-blocking geocode of shipping coordinate metadata for analytics; failure logged, not surfaced

### Acceptance criteria
- **AC-1** Given an authenticated, email-confirmed customer with a product in hand, when they submit the order form with valid name, email, phone, attachments (optional, ≤10 files, ≤10MB each, allowed types), notes, and a chosen Zásilkovna pickup point, then an `Order` is created with the pricing snapshot, the customer is redirected to Comgate, and `Order.State = PendingPayment`.
- **AC-2** Given the order is created, when Comgate returns the payment URL, then the customer is redirected immediately (no 5-second delay screen).
- **AC-3** Given the customer cancels at Comgate or closes the tab, when the order remains in `PendingPayment`, then it can be retried from `/objednavka/<id>` for up to 24 h. After 24 h, the order is auto-cancelled by a background job.
- **AC-4** Given the customer enters an invalid phone or missing field, when the form is submitted, then validation errors render in Czech, mapped from backend `BusinessErrorMessage` codes.
- **AC-5** Given the chosen pickup point is no longer accepting deliveries (rare), when the maker tries to ship, then the customer is notified and given a chance to pick a new point. This is a maker-flow concern (US-maker-NNNN).

### Out of scope
- Multiple items per order (one product per order at MVP).
- Coupon codes / promo codes.
- B2B invoice fields on the order form (deferred — see Q-0004 below if added).

### Related
- ADRs: 0009, 0010, 0016, 0017
- Roles: `order`, `order-pricing`, `order-numbering`, `payment-provider`, `shipping-carrier`, `product`, `maker`

---

## US-customer-0011 — Place an order with personal pickup

### Narrative
As a customer, I want to pick up the order in person at the maker so that I avoid shipping cost and can collect immediately.

### Roles in play
- **Order** — `ShippingMethod = PersonalPickup`; `shipping_price_minor = 0`.
- **Maker** — must have `PersonalPickup = true` and a pickup address.

### Acceptance criteria
- **AC-1** Given the maker offers personal pickup, when the customer chooses "osobní odběr", then the Zásilkovna widget is hidden and the maker's pickup address + note is shown.
- **AC-2** Given personal pickup is chosen, when the order is created, then `shipping_price_minor = 0` and `ZasilkovnaBranchId` is null.
- **AC-3** Given the maker does not offer personal pickup, when the form is rendered, then the personal-pickup radio is disabled with a tooltip.

### Related
- Roles: `order`, `maker`

---

## US-customer-0012 — Track order status

### Narrative
As a customer, I want to see the current state of my order and the timeline of past events so that I know what's happening.

### Roles in play
- **Order** (read)
- **OrderMessage** (read; counts unread)

### Acceptance criteria
- **AC-1** Given the customer visits `/objednavka/<id>`, when the order is owned by them, then they see the order number, current state (Czech label per `ORDER_STATUSES`), product + maker info, pricing breakdown, timeline (placed → paid → accepted → shipped → delivered → completed) with timestamps.
- **AC-2** Given the order is `Shipped`, when the page loads, then a tracking URL link is shown (`https://tracking.packeta.com/...`).
- **AC-3** Given another customer's order id is in the URL, when the visit attempt is made, then the response is 404 (not 403, to avoid leaking existence).
- **AC-4** Given the order is `Shipped` and not yet `Delivered`, when the page loads, then a "Potvrdit doručení" button is visible. Clicking dispatches `MarkOrderDelivered.Command(source: "customer")`.

### Related
- Roles: `order`, `order-message`

---

## US-customer-0013 — Confirm delivery

### Narrative
As a customer, I want to confirm I received the order so the maker gets paid sooner.

### Roles in play
- **Order** — transitions `Shipped` → `Delivered`.

### Acceptance criteria
- **AC-1** Given the order is `Shipped`, when the customer clicks "Potvrdit doručení", then the order transitions to `Delivered` and `DeliveredAt = now()`.
- **AC-2** Given the customer doesn't confirm within 7 days of shipping, when the auto-deliver job runs (daily 08:00 UTC), then the order transitions to `Delivered` automatically (`source = "auto"`).
- **AC-3** Given Packeta confirms delivery via the 6-hourly sync job, when the carrier status flips to `Delivered`, then the order transitions automatically (`source = "carrier"`).
- **AC-4** Given the order is already `Delivered` or beyond, when the confirm action is attempted, then it's rejected with `order.invalidTransition`.

### Related
- ADRs: 0017, 0020
- Roles: `order`

---

## US-customer-0014 — Message the maker about an order

### Narrative
As a customer with a paid order, I want to exchange messages with the maker about the order so we can coordinate details.

### Roles in play
- **OrderMessage** — created with `SenderUserId = customer.UserId`.
- **Order** (read; must be in `Paid` or later state).
- **EmailProvider** (via outbox) — notifies the maker.

### Acceptance criteria
- **AC-1** Given the order is `Paid` or later, when the customer submits a message of ≥1 and ≤2000 characters, then the message is persisted and an outbox event `notification.maker` is enqueued.
- **AC-2** Given the order is in `PendingPayment`, when the customer tries to message, then it's rejected with `order.notPayableYet`.
- **AC-3** Given the maker hasn't read messages for >5 min, when a new message arrives, then a single digest email is sent (debounce; details in maker side US-maker-0011).

### Out of scope
- Read receipts.
- Attachments inside messages (use order-level attachments).
- Typing indicators / real-time.

### Related
- Roles: `order-message`, `order`, `email-provider`

---

## US-customer-0015 — Submit a review after delivery

### Narrative
As a customer with a delivered order, I want to leave a 1–5 star review and optional comment so other customers see what the maker is like.

### Roles in play
- **Review** — created.
- **Maker** — denormalized rating stats updated.
- **Order** (read; must be `Delivered` or `Completed`).

### Acceptance criteria
- **AC-1** Given the order is `Delivered` or `Completed`, when the customer submits rating ∈ [1,5] and an optional comment (≤1000 chars), then a `Review` is created and the maker's `rating_avg` and `rating_count` are atomically updated.
- **AC-2** Given a review already exists for the order, when the customer attempts a second one, then it's rejected with `review.alreadyExists`.
- **AC-3** Given the order is not yet `Delivered`, when a review is attempted, then it's rejected with `review.orderNotDelivered`.

### Out of scope
- Editing a review after submission.
- Star-only review (require rating; comment optional).

### Related
- Roles: `review`, `maker`, `order`

---

## US-customer-0016 — View order list (customer dashboard)

### Narrative
As a customer, I want to see all my past and current orders in a list so I can manage them.

### Roles in play
- **Order** (read; scoped via `IOrderRepository.ForCustomer(userId)`)

### Acceptance criteria
- **AC-1** Given the customer visits `/dashboard/zakaznik`, when the page loads, then their orders are paginated (20 per page) and sorted by `CreatedAt DESC`.
- **AC-2** Given filters (state, date range) are applied, when the list re-loads, then results match the filter combination.
- **AC-3** Given the customer has no orders, when the page loads, then an empty state with a "Browse catalog" CTA is shown.

### Related
- Roles: `order`

---

## US-customer-0017 — Download an invoice

### Narrative
As a customer, I want to download the PDF invoice for any of my orders so I can keep records or file expenses.

### Roles in play
- **Invoice** (read; type=Customer)
- **BlobStorage** (read; streams the PDF)

### Acceptance criteria
- **AC-1** Given the customer owns the order, when they click "Stáhnout fakturu" on the order page, then the PDF streams from `/api/v1/customer/files/invoices/<invoiceId>` with `Content-Disposition: attachment; filename="FV-CZ-NNNNNNNN.pdf"`.
- **AC-2** Given the customer does not own the order, when they request the invoice, then 404.
- **AC-3** Given the order is `Paid` or later, when the page loads, then the invoice download link is visible. Before `Paid`, it isn't.

### Related
- ADRs: 0009, 0011
- Roles: `invoice`, `blob-storage`

---

## US-customer-0018 — Update profile (name, phone)

### Narrative
As a customer, I want to update my name and phone in case they were wrong at registration.

### Roles in play
- **User** — `UpdateProfile.Command` patches `FullName` and `Phone`.

### Acceptance criteria
- **AC-1** Given the customer submits new name/phone, when validation passes, then the user record is updated and `UpdatedBy/On` set.
- **AC-2** Given the customer attempts to change email, when the form is submitted, then they are routed to a separate email-change flow (out of MVP — change request is logged to admin for manual handling).

### Out of scope
- Email change in MVP.

### Related
- Roles: `user`

---

## US-customer-0019 — Change password

### Narrative
As a customer, I want to change my password from inside my account.

### Roles in play
- **AuthService**

### Acceptance criteria
- **AC-1** Given the customer enters current + new password, when current is correct and new passes validation, then the hash is updated and all refresh tokens are revoked (forcing re-login on other devices).
- **AC-2** Given current is wrong, when submitted, then `auth.currentPasswordWrong`.

### Related
- ADRs: 0012
- Roles: `auth-service`

---

## US-customer-0020 — Log out

### Narrative
As a customer, I want to log out so my session ends.

### Roles in play
- **AuthService** — revokes the refresh token.

### Acceptance criteria
- **AC-1** Given the customer clicks "Odhlásit se", when the request hits `/api/v1/customer/auth/logout`, then the refresh token is revoked, the cookie is cleared, and the customer is redirected to `/`.
- **AC-2** Given the customer's session is already expired, when they click logout, then no error — the cookie is cleared anyway, redirect to `/`.

### Related
- ADRs: 0012
- Roles: `auth-service`
