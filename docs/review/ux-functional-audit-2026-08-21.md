# Site-wide functional usability audit — 2026-08-21

Five read-only audits (public / auth / customer / maker / admin surfaces) run for the
site-wide UX functional sweep (Phase 8, tickets T-0166–T-0181). Functional usability
only — no visual/styling findings. Finding IDs below are referenced from the tickets.

Severity: **H** = user is blocked or misled on a core flow, **M** = flow completes but
punishes or confuses, **L** = polish/consistency.

---

## AUTH surface

- **AUTH-H1** — Every transactional email link 404s. `PublicAppUrlsOptions` defaults point at
  `/auth/magic`, `/auth/confirm`, `/auth/reset` (`PublicAppUrlsOptions.cs:28-34`), but the `(auth)`
  route group adds no URL segment — real routes are `/magic`, `/verify`, `/reset`. Only
  `WebBaseUrl` is overridden in any environment (dev appsettings, `app-service.bicep:80`), so
  email-confirmation, magic-link and password-reset round trips are dead in every environment.
  Masked locally because dev never sends real emails (tokens are minted by hand).
- **AUTH-H2** — "Sign in with Google" ends on raw JSON: `google/callback` returns
  `HandleResult(result)` (`AuthController.cs:230-256`), never a redirect; no frontend callback
  page exists. Success sets cookies but strands the user on the API host; failures show bare JSON.
- **AUTH-H3** — Magic link is a dead end for makers: `magic-client.tsx:38,89` hardcodes the
  customer host; `ConsumeMagicLink` rejects the audience with unmapped `auth.forbidden` →
  "K této akci nemáte oprávnění." with no retry/next step (token not burned, so a maker-host
  retry would succeed).
- **AUTH-M1** — `/verify` fires the confirm POST from `useEffect` with no double-fire guard;
  StrictMode/refresh/mail-scanner prefetch burns the one-time token → confirmed users see
  "Odkaz je neplatný" with zero action links (`verify-client.tsx:27-38,71-76`).
- **AUTH-M2** — `auth.emailNotConfirmed` on login is a dead end: the only resend affordance
  (`EmailConfirmationBanner`) requires being logged in, which this user cannot do.
- **AUTH-M3** — Login with a wrong-audience `?redirect=` bounces to the AlreadySignedIn panel:
  `login-form.tsx:55` uses `safeRedirect` verbatim instead of `continueHref` (`route-audience.ts:76-80`).
- **AUTH-M4** — Admin on `/login` gets unmapped fallback copy after dual-host `auth.forbidden`,
  no pointer to `/admin/login` (`login-form.tsx:48-51,134-145`).
- **AUTH-M5** — Terminal 401 in client components shows "Pro pokračování se přihlaste" text with
  no navigation; redirect-to-login-with-returnUrl is per-callsite, not shared (`api-fetch.ts:361-372`).
- **AUTH-M6** — Reset-confirm failure (expired/burned token) renders above a form that can never
  succeed; no link to request a fresh link (`reset-client.tsx:113,164-171`).
- **AUTH-L1** — Register customer/maker tab switch is a `<Link>` navigation that wipes typed input.
- **AUTH-L2** — `?redirect=` is dropped across register/verify/magic funnels; magic consume always
  lands on `/`.
- **AUTH-L3** — `/admin/login` lacks already-signed-in handling and skips `router.refresh()` after login.
- **AUTH-L4** — Resend-banner failure is silent (`email-confirmation-banner.tsx:26-34`).
- **AUTH-L5** — Middleware guard passes on cookie presence, not validity (cosmetic; page-level
  redirects catch it).
- Verified good: pending/disabled submits, input preservation on failure, open-redirect guard,
  dual-host login fallback, 429 copy, logout `AllowAnonymous`.

## PUBLIC surface

- **PUB-H1** — Filter sidebar desyncs from the URL: state seeded once via `useState(initial)`,
  never re-synced, no `key` (`filters-client.tsx:100-107`, `katalog/page.tsx:168-177`, same in
  `[slug]/product-filters-client.tsx:41-43`). Back/forward, "Vymazat filtry" and "Zkusit znovu"
  links change results but the panel keeps stale values.
- **PUB-H2** — No pending feedback on filter/pagination SSR round trips (no `useTransition`);
  pagination scrolls to top of the OLD page before data arrives (`pagination.tsx:62-65`).
- **PUB-H3** — Unfiltered-empty, filtered-empty and out-of-range `?page=99` all render
  "Žádní výrobci neodpovídají vašemu filtru" + "Vymazat filtry"; out-of-range page is a dead end
  (`katalog/page.tsx:221-223,248-264`).
- **PUB-M1** — No `error.tsx` anywhere in `(public)` or at app root; render errors show Next's raw
  English screen (admin/customer/maker groups all have per-route boundaries).
- **PUB-M2** — "Zpět na katalog" links discard filter/page context; product page back link skips
  the maker profile the user came from.
- **PUB-M3** — Catalog error "Zkusit znovu" links bare `/katalog` — doesn't retry the failed
  request, drops filters/page.
- **PUB-M4** — Reviews cap at 5 with badge = `reviews.length` while the seller panel shows the
  true `ratingCount` on the same screen; no "more" affordance (`reviews-section.tsx:25-29`).
- **PUB-M5** — Landing category tiles hardcoded (`app/page.tsx:24-31,130-147`); dropped/renamed
  category slugs silently show the full unfiltered catalog.
- **PUB-M6** — Logout failure is completely silent (`public-navbar.tsx:67-83`).
- **PUB-M7** — Filters `router.replace` vs pagination push; JSDoc claims back-restore that
  `replace` can't provide.
- **PUB-L1** — Root 404 renders without site navigation (root layout has no navbar/footer).
- **PUB-L2** — Maker-profile/product loading skeletons mirror an outdated layout → visible jump.
- **PUB-L3** — ScrollToTop exists only on `/katalog`, not the equally long maker profile.
- **PUB-L4** — Maker/product transient-error surfaces offer no retry, only "Zpět na katalog".
- **PUB-L5** — Account dropdown: no Escape-close, no focus return.
- **PUB-L6** — Signed-in **admin** gets the customer "Objednat" CTA → unsatisfiable login loop
  (the trap fixed for makers in 49b3637 remains open for the admin audience)
  (`produkt/[productId]/page.tsx:126`, `objednavka/page.tsx:73-81`).
- **PUB-L7** — Middleware login redirect drops the query string (`middleware.ts:170`).
- **PUB-L8** — `/produkt/*` highlights no navbar item.

## CUSTOMER surface

- **CUST-H1** — Pre-payment order page renders attachments as plain `<a href={downloadUrl}>` with a
  backend-relative path → 404 on the frontend origin in every environment; navigation also destroys
  in-memory failed-upload retries (`attachment-manager-client.tsx:146-151`). The tracking surface
  correctly uses `FileDownloadButton`.
- **CUST-H2** — Checkout's unconfirmed-email error says "resend from your profile", but the profile
  has no resend; `EmailConfirmationBanner` implements it and is mounted **nowhere**.
- **CUST-H3** — Checkout failure feedback lands off-viewport: errors render at the top of a long
  form, `noValidate` disables native focusing, nothing scrolls/focuses (`order-form-client.tsx:153-169`).
- **CUST-H4** — Checkout prefills only email; profile `fullName`/`phone` are fetched SSR and unused —
  returning customers retype both every order (`objednavka/page.tsx:121-129`).
- **CUST-M1** — Both profile pages render raw `result.error.message` on SSR failure, no retry, no
  Unauthorized→login redirect (`profile/page.tsx:28-32`; maker twin `profil/page.tsx:33-36`).
- **CUST-M2** — Confirmation page: stale "detail připravujeme" banner (detail shipped in T-0086b);
  `?status=` failure evaluated before the state check → already-cancelled orders show a FailureView
  promising a pay button the detail doesn't have (`potvrzeni/page.tsx:111-136`).
- **CUST-M3** — A customer cannot cancel an unpaid order; only exit is the silent 24 h auto-cancel.
- **CUST-M4** — Checkout loses all entered data on refresh/back/tab-close; no dirty guard, no draft.
- **CUST-M5** — Payment poller burns its last tick without polling (~27 s effective); failed payment
  leaving PendingPayment never reaches the failure/retry frame (`payment-poll-client.tsx:65-81`).
- **CUST-M6** — Login redirects drop the query string (middleware + orders list `ROUTE_PATH`).
- **CUST-L1** — Checkout field errors clear only on next submit, not on change.
- **CUST-L2** — Order-list error retry links bare route → wipes filters.
- **CUST-L3** — Stale `?attachmentsFailed=N` warning survives successful retries.
- **CUST-L4** — Order detail/list never link to the product or maker (plain text).
- **CUST-L5** — Dispute escalation form permanently expanded under every Paid+ thread, contradicting
  its own "write to the maker first" intro.
- Verified good: skeletons on all customer routes, distinct empty states, message-thread logic,
  `SaveButton` on profile, URL-state pagination, double-submit guards, backend-state-gated confirmation.

## MAKER surface

- **MAKER-H1** — Review reply form locks up permanently after a successful submit: success path
  never resets `submitting`/`inFlightRef`; `router.refresh()` doesn't remount the island
  (`reply-form.tsx:39-56`).
- **MAKER-H2** — Product image upload uses the 8 s default timeout (`maker-products.ts:253-264`) —
  the documented apiFetch-upload trap; failure maps to misleading "invalid file" copy. Avatar/logo
  uploads pass `UPLOAD_TIMEOUT_MS`; this one doesn't.
- **MAKER-H3** — No decline/reject path for a Paid order — accept or ignore are the only options,
  no guidance what to do when the maker can't fulfil.
- **MAKER-H4** — Soft-deleted product has no reactivation path; inactive card still offers "Smazat";
  confirm copy doesn't say it's unrecoverable.
- **MAKER-M1** — Product form save confirmation renders off-viewport at the top; stale success flash
  never clears; no dirty tracking; plain button instead of the shared `SaveButton`
  (`product-form.tsx:169-171,213-216,333-344`).
- **MAKER-M2** — Unsaved product-form changes lost silently on any navigation (no guard, create+edit).
- **MAKER-M3** — Payouts page can't answer "how much am I owed and when": no accrued balance, no
  expected date, no cadence explanation (`vyplaty/page.tsx:84-114`).
- **MAKER-M4** — Missing bank account never surfaced on /vyplaty or dashboard-wide.
- **MAKER-M5** — New orders / unread messages invisible outside the orders list; no nav/tab badges;
  unread badge on a Shipped order hidden by the default "Nové" tab.
- **MAKER-M6** — Image manager: no primary/cover selection or reorder, single-file picker (8 photos =
  8 cycles), 10-image cap discoverable only via 409 (`image-manager.tsx:103-188`).
- **MAKER-M7** — Create → edit transition silent: no "Produkt vytvořen" notice, no hint the product is
  already live, no public-page link.
- **MAKER-L1** — Bare `/dashboard/maker` 404s (no page.tsx; `audienceHome` covers login only).
- **MAKER-L2** — Order-list error retry drops tab/filters (bare `ROUTE_PATH`).
- **MAKER-L3** — Date filters push per keystroke; inverted range → full-list backend 400 instead of
  inline hint.
- **MAKER-L4** — Tabs cover only Paid/Accepted/all; no Shipped/Completed/Cancelled filter, no search.
- **MAKER-L5** — Out-of-range deep-linked page shows a misleading "all clear" empty state.
- **MAKER-L6** — Reviews reply form always open (edit/cancel i18n keys exist unused); inactive
  products clutter the grid forever (no active filter).
- Verified good: order-action double-submit guards + Conflict reconciliation, confirm-gated ship,
  per-tab empty states, payout detail breakdown, profile `SaveButton`, message thread.

## ADMIN surface

- **ADM-H1** — "Uživatelé" is a blind GDPR-erase form: no user lookup/list exists; phase-1 "lookup"
  verifies nothing (matches the admin's own typed email); the strongest destructive flow runs on
  copy-pasted identifiers (`users/page.tsx:20-28`, `delete-user-panel.tsx:64-161`).
- **ADM-H2** — Order-detail audit trail fetches the *global* order audit slice and filters
  client-side → can show empty/incomplete history on the dispute-triage evidence surface
  (`admin-orders.ts:345-377`).
- **ADM-H3** — Expired session: makers + kategorie show a dead-end alert instead of the
  login redirect every other admin route does.
- **ADM-H4** — Kategorie and the 6-probe Overview have no `loading.tsx`/`error.tsx` and no boundary
  up the tree.
- **ADM-H5** — Maker/category deactivation is one-way (no reactivate in UI or helpers) behind a
  lightweight two-click arm-confirm.
- **ADM-H6** — Category row bricks itself after a successful deactivate: success path never resets
  `busy`; Edit stays disabled until hard reload (`category-row.tsx:57-72`).
- **ADM-M1** — Five parallel pagination implementations; drift already visible (order-detail copy
  lacks the page indicator).
- **ADM-M2** — Makers search uses client `router.replace` (no history entry, no reset link) vs the
  GET-form pattern of the other three list filters.
- **ADM-M3** — Inline "Zkusit znovu" links drop all active filters (orders/faktury/audit).
- **ADM-M4** — Detail pages return to unfiltered lists; triaging N orders means rebuilding filters N times.
- **ADM-M5** — Outbox retry/ack success notice renders inside the row that `router.refresh()`
  immediately unmounts.
- **ADM-M6** — Refund, manual state change, complete-batch end with **no** success confirmation —
  the highest-stakes actions violate the in-viewport-confirmation rule.
- **ADM-M7** — No cross-links between related entities; id filters require manual GUID copying.
- **ADM-M8** — Audit rows dead-end (promised detail route never built); long notes truncated with
  no tooltip/expansion.
- **ADM-M9** — Typo'd user id in the GDPR flow reported as "uživatel již byl smazán" — false
  compliance signal (`delete-user-panel.tsx:238-239`).
- **ADM-M10** — Three modal shells claim a focus trap and have none.
- **ADM-L1** — Filtered-to-zero lists show the generic "nothing exists" empty state, no reset CTA.
- **ADM-L2** — Return-label card: both buttons share one `busy` spinner; generated label unreachable;
  generate stays enabled.
- **ADM-L3** — Arm-confirm never disarms (no outside-click/timeout).
- **ADM-L4** — Fee-override form never prefills the existing override.
- **ADM-L5** — `pageSize` honored on 3 lists, ignored on 3; page clamp missing.
- **ADM-L6** — Blob download errors are context-free booleans (401 = 500 = timeout).
- **ADM-L7** — `orders/[orderId]` throws bubble to the list's error.tsx with misdirecting copy.
- **ADM-L8** — Admin actors render as raw GUIDs everywhere.
- Cross-cutting inventory: 5× pagination, 3+1 filter bars, 3× modal shell, 2× blob download,
  2× arm-confirm; loading/error present everywhere except kategorie + overview; Unauthorized
  redirect everywhere except makers + kategorie.
