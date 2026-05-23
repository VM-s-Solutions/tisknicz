---
id: T-0015
title: Frontend scaffold — route groups, runtime helpers (Result + apiFetch), auth session shape, cs-CZ catalog, middleware
status: done
size: M
owner: frontend
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0013]
blocks: []
adrs: [0005, 0007, 0012, 0022]
phase: 1
---

# T-0015 — Frontend scaffold

## Scope

### New scaffolding (the heart of this ticket)
- `frontend/src/lib/runtime/result.ts` — TypeScript counterpart of `BusinessResult<T>` + `ErrorType` + `ApiError`; `ok`/`err`/`isOk`/`isErr` helpers.
- `frontend/src/lib/runtime/api-fetch.ts` — `apiFetch<TValue>(host, path, options)` — the single boundary every NSwag client call goes through. Maps HTTP responses → `Result<T, ApiError>`, handles JWT cookie auth, applies an 8 s timeout, preserves `x-correlation-id`, and produces Czech-localized fallback error messages.
- `frontend/src/lib/runtime/index.ts` — barrel.
- `frontend/src/lib/auth/session.ts` — `Audience`/`Role`/`Session` types; audience-scoped cookie name helpers (`accessCookieName`, `refreshCookieName`). Phase 1 establishes the contract; T-0027 wires real JWT validation.
- `frontend/src/lib/auth/index.ts` — barrel.
- `frontend/src/lib/i18n/cs-CZ.ts` — Czech message catalog scaffold with `t(key, params?)` helper; covers common/error/auth/nav/catalog/order-state keys.
- `frontend/src/lib/i18n/index.ts` — barrel.
- `frontend/src/lib/api-client-helpers/README.md` — explains the consumer-side wrapper convention (one file per feature; routes through `apiFetch`).
- `frontend/src/app/(public)/layout.tsx`, `(auth)/layout.tsx`, `(customer)/layout.tsx`, `(maker)/layout.tsx`, `(admin)/layout.tsx` — five route-group layouts per ADR 0005. Each is a thin pass-through; chrome/navigation lands with the audience-owning tickets (T-0035 / T-0036 / T-0118).
- `frontend/src/middleware.ts` — rewritten as a JWT-cookie placeholder. Infers audience from the URL (`/dashboard/zakaznik/*` → customer, `/dashboard/maker/*` → maker, `/dashboard/admin/*` → admin) and redirects to `/auth/login` when the audience-scoped access cookie is missing. Real signature validation lands in T-0027.

### Side-deliverable: ADR 0007 follow-through (pre-pivot Supabase rip-out)
Per ADR 0007 the project pivoted from Supabase to a .NET backend. The Phase-1 frontend still contained legacy Supabase-bound page templates and route handlers that no longer compile because the .NET backend hasn't yet shipped equivalent endpoints. They block typecheck and confuse future tickets about what the source of truth is. Deleted in this ticket:

- `src/app/api/**` — Next route handlers wrapping Supabase queries (replaced by direct calls into the .NET hosts via NSwag clients, per ADR 0022)
- `src/app/auth/**`, `src/app/dashboard/**`, `src/app/katalog/**`, `src/app/produkt/**`, `src/app/objednavka/**` — pre-pivot page templates; Phase-2/3/4 tickets ship the .NET-backed replacements
- `src/components/catalog/**`, `src/components/dashboard/**`, `src/components/forms/**`, `src/components/layout/**` — pre-pivot components tied to Supabase types
- `src/lib/ares/**` and `src/lib/demo-data.ts` — pre-pivot ARES client and demo data; ARES lives in `backend/.../Infra.Clients/Ares` now (T-0032)
- Removed `<Header />` / `<Footer />` from `src/app/layout.tsx`; removed `<MakerSignupForm />` import from `src/app/pro-makery/page.tsx` (replaced by a "Registrační formulář je v přípravě." note until T-0033/T-0035).

The shared visual primitives (`src/components/ui/*`, `src/components/shared/hero-scene*`) are kept — they're framework-neutral and Phase-2/3 will adopt them.

## Out of scope
- Real authentication / JWT signing (T-0027).
- The first real api-client-helpers file (deferred until the first `*-api.v1.ts` generator output exists — T-0035 makes that real).
- Header / footer / sidebar components (T-0035 / T-0036).
- Rebuilding the catalog / order / dashboard pages on top of the .NET backend (T-0046+, T-0084+, T-0086+).

## Acceptance criteria
- **AC-1** `npx tsc --noEmit` is clean in `frontend/`.
- **AC-2** `npx eslint` on the T-0015 surface (`lib/runtime`, `lib/auth`, `lib/i18n`, `middleware.ts`, the five route-group layouts) is clean.
- **AC-3** `npx next build` succeeds; the route map lists the surviving public pages.
- **AC-4** `apiFetch` maps every standard HTTP status (400/401/403/404/409/422/429/5xx) to a stable `ApiError.type` and a Czech `message`.
- **AC-5** The middleware redirects unauthenticated `/dashboard/*` requests to `/auth/login?redirect=...` and is matcher-scoped to those paths only.
- **AC-6** Pre-pivot Supabase code is removed; no `@supabase/*` or `@/lib/supabase` imports remain.

## Status log
- 2026-05-23 done. Typecheck + build clean. Pre-pivot Supabase rip-out applied.
