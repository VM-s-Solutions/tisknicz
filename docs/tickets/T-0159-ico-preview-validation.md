---
id: T-0159
title: IČO autocomplete + Czech validation on maker registration (business decision Q4)
status: in_review
size: M
owner: dotnet-backend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0032, T-0033, T-0124, T-0136]
blocks: []
user_stories: [US-maker-0001]
adrs: [0018, 0022, 0008]
phase: 7
manual_steps: [nswag-regen]
security_touching: true
layers: [dotnet-backend, frontend, l10n]
---

# T-0159 — IČO preview + Czech validation

## Context

Operator request ("make autocomplete on ICO so it is right and make
validation ICO should be CZECH") — which is exactly business decision
**Q4** from the 2026-07-04 meeting: *"IČO → ARES předvyplní, maker potvrdí
správnost."* Until now the form accepted any 8 characters and the user
learned whether the IČO was right only after submitting; dopady §2.3 pt 4
explicitly asked to verify this UX.

## Scope

- **Backend `LookupCompanyPreview`** (one-file feature) + anonymous
  `GET /api/v1/makers/registry-preview?registrationNumber=&countryCode=`
  on the Public host's `RegisterMakerController`. Mod-11 gate BEFORE the
  registry call (ADR 0018 §"Validation before lookup" — garbage never
  spends ARES budget); registry resolved per country via the T-0124
  factory; returns the display slice (name, legal form, DIČ, address,
  active/stale flags). Errors pass through pre-classified
  (`company.notFound` / transient / permanent). Rate-limited with the
  tight per-IP `"auth"` bucket (T-0136) — anonymous enumeration-adjacent
  surface; the client debounces to ≤1 call per typed IČO. **Response
  record named `LookupCompanyPreviewResponse`** — a nested record named
  plain `Response` generates a TS class that shadows the DOM `Response`
  in the NSwag client (T-0076/T-0080 precedent, tripped and fixed here).
- **Frontend `lib/validation/czech-ico.ts`** — TS mirror of the backend
  `CzechIcoValidator` mod-11 checksum + digit-only input normalisation
  (strips spaces/prefixes, caps at 8).
- **Form UX** (`register-maker-form`): input normalised as you type;
  checksum failure shows an inline Czech error and blocks submit
  locally; a valid IČO triggers a 400 ms-debounced preview
  (event-handler-driven, no effect fetch; stale responses dropped via a
  sequence counter) rendering the ARES company card — name, legal form,
  address, DIČ, "zkontrolujte, že registrujete správnou firmu", with a
  dissolved-company error and a not-found warning. **A failed preview
  never blocks submission** — registration re-runs the authoritative
  lookup server-side (this endpoint is UX, not a gate).
- NSwag public client regenerated in the same PR (spec-parity CI green).
- 9 new i18n keys under `auth.register_maker.*`.

## Acceptance criteria

- **AC-1** Given the IČO field, when the user types `CZ270 743 58`, then
  the input normalises to `27074358` and, being checksum-valid, shows the
  ARES card for Avast Software s.r.o.-style data (name, address, DIČ).
- **AC-2** Given an 8-digit input with a bad mod-11 checksum, when typed,
  then an inline Czech error appears, no ARES call is made, and submit is
  blocked locally with the same message.
- **AC-3** Given an IČO unknown to ARES, when previewed, then the
  "nenalezena" warning shows; given ARES down, then the non-blocking
  "neblokuje registraci" note shows and submit still works.
- **AC-4** Given a dissolved company, when previewed, then the dissolved
  error shows (registration would be rejected server-side anyway).
- **AC-5** Given the endpoint, when called anonymously in a burst, then
  the per-IP auth rate-limit bucket (10/min) applies.

## Test plan reference

Backend: 6 new handler tests (display-slice mapping, checksum gate
without registry spend ×2, registry/factory failure passthrough,
dissolved+stale flags) — suite 1899/1899. Frontend: 11 new vitest cases
(real-IČO checksums incl. ministry + ČEZ, rejections, normalisation) —
suite 87/87. tsc/eslint/next build clean; NSwag regen verified (tsc 0
after the Response-shadowing fix).

## Status log

- 2026-07-23 `draft → in_progress → in_review` — implements business
  decision Q4; PR merged under the operator's standing authorization.
