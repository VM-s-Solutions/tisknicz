---
id: 0005
title: Per-audience route groups; customer-as-authenticated audience
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0005 — Per-audience route groups; customer-as-authenticated audience

## Context
The MVP serves four audiences (public visitors, authenticated customers, makers, admins) plus integration callers (Comgate, Packeta, Vercel Cron). Each audience has different auth rules, CORS posture, rate-limit budgets, and trust assumptions. We need an organizing principle that makes those differences explicit and enforceable.

## Decision
Adopt `patterns.md` §16. Next.js route groups define the audience boundary; middleware applies per-group policy.

### Route group layout

```
src/app/
├── (public)/                      # marketing, catalog, product detail, jak-to-funguje, vop, gdpr, pro-makery
├── (auth)/                        # /auth/login, /auth/register, /auth/callback, /auth/reset
├── (customer)/                    # /dashboard/zakaznik/*, /objednavka/*
├── (maker)/                       # /dashboard/maker/*
├── (admin)/                       # /dashboard/admin/*
└── api/
    ├── public/                    # ARES proxy (rate-limited), webhooks/comgate, webhooks/packeta, cron/*
    ├── customer/                  # customer-scoped API
    ├── maker/                     # maker-scoped API
    └── admin/                     # admin-scoped API
```

### Customer is an authenticated audience

- **No guest checkout at launch.** A customer must register (or magic-link sign-in) before placing an order.
- Order placement (`POST /api/customer/orders`) requires a session.
- The (auth) flow includes a "Continue as guest" CTA that simply opens the magic-link form — same screen, lower friction wording.
- Rationale: keeps RLS scoping clean (`orders.customer_id` is always a real user id); eliminates the "claim your order" post-purchase flow; simplifies invoicing (we always have a billable identity); makes maker–customer messaging coherent from the start.

### Middleware

`src/middleware.ts` dispatches by path prefix:
- `(public)` and `/api/public/*` — no auth required; webhooks verify origin/signature; ARES proxy rate-limited per IP (10/min per `patterns.md` §14).
- `(auth)` — no auth required, redirects to dashboard if already signed in.
- `(customer)` and `/api/customer/*` — requires session with `role IN ('customer', 'admin')`. Admin can impersonate (read-only) for support.
- `(maker)` and `/api/maker/*` — requires session with `role IN ('maker', 'admin')`. Maker can only access their own maker record (enforced by RLS).
- `(admin)` and `/api/admin/*` — requires session with `role = 'admin'`. Stricter rate limit; all responses include `Cache-Control: no-store`.

### Webhooks

Live under `/api/public/webhooks/<provider>/`:
- `/api/public/webhooks/comgate` — POST, verifies Comgate source IP allowlist, re-fetches payment status via Comgate API before acting (`patterns.md` §20).
- `/api/public/webhooks/packeta` — POST, verifies signature.
- `/api/public/cron/<job>` — POST, requires `Authorization: Bearer ${CRON_SECRET}`.

## Alternatives considered

- **Guest checkout** — rejected by user. Lower friction but adds a "claim your order" flow, complicates RLS, splits the identity model.
- **OAuth-only login** (Google/Apple) — rejected. Czech market still leans email/password; OAuth is post-MVP.
- **Single `(app)` group with role checks inside pages** — rejected. Implicit auth boundaries are easier to break and harder for SecOps to audit.
- **Subdomain per audience** (customer.makables.cz, maker.makables.cz, admin.makables.cz) — rejected for MVP. Cleaner separation but adds DNS, cert, and CORS complexity. Reconsider if traffic patterns warrant it.

## Consequences

- **Positive:** every API endpoint has an obvious audience from its URL. SecOps audit is a path-prefix sweep.
- **Positive:** RLS policies + middleware enforce defense in depth (middleware blocks the wrong role; RLS blocks cross-tenant reads even if middleware is wrong).
- **Positive:** rate-limit and CORS rules diverge per audience cleanly (admin can be locked down; public ARES proxy stays open with throttling).
- **Negative:** higher checkout friction without guest checkout. Mitigated by magic-link option that needs only an email.
- **Negative:** four middleware paths to maintain. The dispatch is a single `switch (pathPrefix)` so the cost is small.

## Compliance / verification

- Reviewer checklist: new API route is placed under the correct `/api/<audience>/` prefix.
- Reviewer checklist: pages are placed under the correct `(audience)` route group.
- SecOps audit: middleware `switch` covers every prefix; default returns 404, not "fall through to next" behavior.
- SecOps audit: webhooks under `/api/public/webhooks/*` verify origin/signature before any side effect.
- SecOps audit: cron endpoints check `Authorization` header.

## Related
- Patterns: §16 Per-audience route groups, §19 RLS, §20 Idempotent webhooks
- Depends on: ADR 0001 (layering)
- Will be referenced by: every Route Handler ticket
