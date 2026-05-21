# Reviewer checklist

Run this for every PR. If a row fails, request changes — do not approve.

## A. CLAUDE.md self-check
- [ ] No `any` types; no unsafe `!`
- [ ] All function params and returns typed
- [ ] No `console.*`
- [ ] No TODO/FIXME without context
- [ ] No unused imports / dead code
- [ ] No commented-out code blocks

## B. Architecture
- [ ] Server Components by default; `'use client'` only with justification
- [ ] No data fetching via `useEffect` in client components
- [ ] External APIs called only through `src/lib/<provider>/`
- [ ] Route Handlers validate input with Zod
- [ ] Auth check present on every protected route
- [ ] No raw `fetch()` to third-party APIs outside `lib/`

## C. Domain & extension points
- [ ] If touching payments, shipping, tax, locale, address, or money: respects the relevant ADR
- [ ] No country/provider-specific code outside its adapter
- [ ] Money is integer minor units (per ADR)

## D. Security
- [ ] RLS enabled on any new table; policies cover all roles
- [ ] File uploads validated (type + size) server-side
- [ ] No secrets in client bundle (only `NEXT_PUBLIC_*`)
- [ ] Webhook endpoints verify origin / signature
- [ ] Cron endpoints check `CRON_SECRET`

## E. UI/UX
- [ ] Responsive: 375 / 768 / 1280
- [ ] Loading + error states present for async UI
- [ ] Czech copy used; no English strings leaked
- [ ] No inline `style={}` for layout/spacing
- [ ] Uses `components/ui/` primitives

## F. AC traceability
- [ ] Every AC item in the ticket has a corresponding change in the diff
- [ ] PR description lists AC items addressed

## G. Tests & docs
- [ ] New pure logic has unit test
- [ ] Test plan executed by QA
- [ ] Docs updated if architecture/process/env changed
