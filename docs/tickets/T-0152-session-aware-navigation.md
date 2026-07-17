---
id: T-0152
title: Session-aware navigation + dashboard chrome + maker login fix + Apple sign-in UI removal
status: done
size: M
owner: frontend
created: 2026-07-17
updated: 2026-07-17
depends_on: [T-0035, T-0036, T-0086a, T-0087a, T-0116, T-0117]
blocks: []
user_stories: [US-customer-0016, US-customer-0018, US-maker-0005, US-maker-0015]
adrs: [0005, 0012, 0022]
phase: 7
manual_steps: []
security_touching: true
layers: [frontend]
---

# T-0152 — Session-aware navigation + dashboard chrome + maker login fix + Apple sign-in UI removal

## Context

Logging in appeared to "do nothing": the public navbar was fully static (always
"Přihlášení"), nothing anywhere reflected the session, and the customer/maker
dashboard layouts were Phase-1 skeletons with zero navigation — every maker
capability that already shipped (orders incl. accept/ship, product CRUD,
payouts, review replies, ARES profile) was unreachable except by typing URLs.
Worse, makers could not log in at all: the login form hardcoded the customer
API host and the backend's `User.MatchesAudience` rejects a maker account on
the customer host with `auth.forbidden` (403). Separately, the operator decided
to drop Apple Sign-In from the product (Apple Developer Program cost/benefit),
so the T-0139 UI surface comes out.

## Scope

- **Apple sign-in UI removal**: `AppleSignInButton` component + test deleted;
  button removed from login + register forms; `startAppleOAuth` helper,
  `apple` icon glyph, and `auth.apple.*` i18n keys removed (`orDivider` moved
  to the provider-agnostic `auth.oauth.orDivider`). Frontend only.
- **`lib/auth/display-session.ts`**: server-only reader that decodes the
  audience-scoped access-JWT cookie (maker before customer) into a
  display-only `DisplaySession { userId, email, audience }`. No signature
  verification by design — authorization stays with the edge middleware +
  backend; a forged cookie can only mislead its own browser.
- **Session-aware `PublicNavbar`**: new optional `session` prop; renders a
  "Můj účet" dropdown (email header, role-specific links, logout) instead of
  the login/start-selling CTAs, desktop + mobile. Logout calls the audience
  host, then `router.push('/') + router.refresh()`.
- **Login fix**: the form tries the customer host and retries the maker host
  on `auth.forbidden` (order flips when `?redirect=` targets
  `/dashboard/maker/*`); makers land on `/dashboard/maker/objednavky`;
  `router.refresh()` after success so server-rendered chrome picks up the
  fresh cookie.
- **Dashboard chrome**: new `DashboardNav` (horizontal section tabs, active
  state via `usePathname`) + nested layouts
  `app/(customer)/dashboard/zakaznik/layout.tsx` (Objednávky / Profil) and
  `app/(maker)/dashboard/maker/layout.tsx` (Objednávky / Produkty / Výplaty /
  Recenze / Profil), both mounting the session-aware navbar.
- Session passed into the navbar from the `(public)` + `(auth)` layouts and
  the landing page (which is now request-rendered instead of static — the
  cost of cookie-aware chrome).
- New `nav.*` i18n keys; `auth.oauthNotAllowedForAdmin` copy no longer
  mentions Apple.

## Alternatives Considered

- **Client-side session detection via `GET /api/v1/me` on mount** — *rejected:
  flash-of-logged-out on every page, an extra API round trip per navigation,
  and it breaks entirely when the backend is down (dev today), whereas the
  cookie decode is local.*
- **Non-HttpOnly "session hint" cookie set by the backend** — *rejected:
  backend contract change for a display concern; ADR 0012 keeps all session
  cookies HttpOnly.*
- **Removing the Apple backend stack (endpoints, `User.AppleSub`, ADR 0026) in
  the same PR** — *rejected for this ticket: needs an EF migration, an ADR
  supersession, and an NSwag regen; the UI removal already makes the flow
  unreachable. Follow-up if the operator wants the code gone.*

## Out of scope

- Backend Apple OAuth removal (endpoints stay, unreachable from the UI; the
  generated `api-client/` methods disappear only on the next NSwag regen).
- Admin area chrome (`/admin/login` + admin dashboard have their own surface,
  T-0118a).
- Cross-domain cookie strategy for `*.azurewebsites.net` (pre-existing
  constraint of SSR cookie forwarding; needs a shared parent domain — tracked
  by deploy/ops, not this ticket).

## Acceptance criteria

- **AC-1** Given a logged-out visitor on any public page, when the page
  renders, then the navbar shows "Přihlášení" + "Začít prodávat" and no
  account menu.
- **AC-2** Given a customer with valid credentials, when they submit the login
  form, then exactly one login request goes to the customer host, they are
  redirected to `/` (or the `?redirect=` target), and the navbar immediately
  shows the "Můj účet" menu with their email, "Moje objednávky", "Můj profil"
  and "Odhlásit se".
- **AC-3** Given a maker with valid credentials, when they submit the same
  login form, then the customer-host 403 (`auth.forbidden`) triggers a retry
  against the maker host, they land on `/dashboard/maker/objednavky`, and the
  account menu lists Objednávky / Produkty / Výplaty / Recenze / Profil.
- **AC-4** Given a maker with a wrong password, when they submit, then no
  second-host retry happens (credential errors precede the audience check) and
  the "Nesprávný e-mail nebo heslo." message shows.
- **AC-5** Given a logged-in user, when they open any `/dashboard/...` page in
  their area, then a persistent section navigation renders with the current
  section highlighted.
- **AC-6** Given a logged-in user, when they click "Odhlásit se", then the
  audience host's logout endpoint is called, cookies clear, they land on `/`,
  and the navbar reverts to the logged-out state without a hard reload.
- **AC-7** Given the login and register pages, when they render, then no Apple
  button, icon, or copy exists anywhere in the frontend bundle.
- **AC-8** Given a hand-crafted (unsigned) access cookie, when a page renders,
  then at most the navbar display is affected — every dashboard data fetch
  still 401s at the backend (display-only decode, no authorization derived).

## Technical notes

- JWT payload decode uses `Buffer.from(part, 'base64url')` in the server-only
  module; the client navbar imports only the *type* (`import type`), so
  nothing server-bound reaches the client bundle.
- `router.refresh()` after login/logout is load-bearing: App Router soft
  navigation would otherwise keep serving the layout tree rendered with the
  old cookie state.
- Login host fallback keys on the exact `BusinessErrorMessage.AuthForbidden`
  code (`auth.forbidden`).

## Files touched (expected)

- `frontend/src/lib/auth/display-session.ts` (new)
- `frontend/src/components/shared/public-navbar.tsx`
- `frontend/src/components/shared/dashboard-nav.tsx` (new)
- `frontend/src/app/(customer)/dashboard/zakaznik/layout.tsx` (new)
- `frontend/src/app/(maker)/dashboard/maker/layout.tsx` (new)
- `frontend/src/app/(public)/layout.tsx`, `frontend/src/app/(auth)/layout.tsx`, `frontend/src/app/page.tsx`
- `frontend/src/app/(auth)/login/login-form.tsx`, `frontend/src/app/(auth)/register/register-form.tsx`
- `frontend/src/components/shared/apple-sign-in-button.{tsx,test.tsx}` (deleted)
- `frontend/src/lib/api-client-helpers/auth.ts`, `frontend/src/lib/i18n/cs-CZ.ts`, `frontend/src/components/ui/icon.tsx`

## Test plan reference

Covered by the existing suite (64 tests green incl. updated
`google-sign-in-button.test.tsx`) + `tsc --noEmit` + `next build`. Runtime
E2E (AC-2/3/6) pends the dev backend being reachable — fold into the
T-0153 end-to-end pass.

## Status log

- 2026-07-17 `draft → in_progress → done` — implemented and merged in one pass
  per direct user request (items 1–4 of the 2026-07-17 session); ticket filed
  retroactively for traceability.
