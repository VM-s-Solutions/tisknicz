---
id: 0001
title: Four-layer architecture (domain / features / infra | runtime / app)
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0001 — Four-layer architecture

## Context
Makables needs an architecture that (a) keeps business logic testable in isolation, (b) absorbs multi-country and multi-provider variation behind interfaces, (c) is recognizable to a developer with .NET Clean Architecture background, and (d) is idiomatic in Next.js 16 + Supabase.

## Decision
Adopt the four-layer architecture defined in `docs/architecture/patterns.md` §1 verbatim. Layers and dependency direction:

```
app (Next.js Route Handlers, pages) ─► runtime ─► features ─► domain ◄─ infra
```

- **`src/lib/domain/`** — pure TypeScript. Entities, value objects, repository interfaces, `Result`, `AppError`, error codes, the `Money` value object. No imports from `infra/`, `features/`, `runtime/`, `app/`, or any third-party SDK.
- **`src/lib/features/`** — use case handlers. Imports only from `domain/`. Never imports `@supabase/*`, Comgate, Packeta, etc.
- **`src/lib/infra/`** — Supabase repositories and integration adapters. Implements interfaces declared in `domain/`. The only place third-party SDKs are imported.
- **`src/lib/runtime/`** — pipeline middleware (`withAuth`, `withValidation`, `withLogging`, `withTransaction`), `handleResult`, `makeContext`, the DI container wiring. Depends on `domain/` and `features/`.
- **`src/app/`** — Next.js Route Handlers and pages. Stays thin: parse input, call composed pipeline, return response.

## Alternatives considered

- **Single flat `src/lib/` with no layering** — rejected. Works for tiny apps; becomes spaghetti as integrations multiply.
- **Hexagonal / Ports & Adapters terminology** — rejected. Same idea but unfamiliar; "domain / features / infra / runtime" reads cleaner to both the .NET-trained user and TypeScript readers.
- **Co-locate everything by feature (`src/features/orders/{domain,api,ui}`)** — rejected. Feature-first folders sound nice but make cross-feature reuse (Money, AppError, Result) awkward and tend to drift into duplication. The patterns catalog already feature-folders *within* `features/`, which is the right granularity.

## Consequences

- **Positive:** clean test seams (mock repos in `features/` tests; no Supabase needed). Multi-country variation absorbed in `infra/`. Refactoring storage (Supabase → something else) is mechanical, not a rewrite.
- **Positive:** matches the user's mental model from `Cleansia.Core.Domain` / `Cleansia.Core.AppServices` / `Cleansia.Infra.*`.
- **Negative:** four layers feel heavy for an MVP. Mitigation: `runtime/` is small (~6 files); `domain/` and `features/` are the only layers where most work happens.
- **Negative:** boilerplate per use case is higher than a direct `app/api/orders/route.ts` with inline logic. Accepted as the cost of preserving the extension seam.

## Compliance / verification

- ESLint rule: `src/lib/domain/**` must not import from `src/lib/infra/**`, `src/lib/features/**`, `src/lib/runtime/**`, `src/app/**`, `@supabase/*`, or any third-party SDK package (see `.eslintrc` boundaries config to be added by SecOps in ticket).
- ESLint rule: `src/lib/features/**` must not import from `src/lib/infra/**` or `@supabase/*`.
- Reviewer checklist item: any new `@supabase/*` import must be in `src/lib/infra/`.

## Related
- Patterns: §1 Layered architecture
- Will be enforced by ADR for ESLint boundary rules (TBD by SecOps + DB during initial-scaffold ticket)
