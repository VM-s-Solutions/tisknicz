# Sprint 1 — status

**Period:** 2026-05-20 → 2026-05-23
**Goal (per `INDEX.md`):** *"Solution scaffolded, hosts run, OpenAPI emitted, NSwag pipeline works, Bicep deploys an empty environment."*
**Outcome:** **Goal met.** All 16 Phase-1 tickets shipped.

## Ticket summary

| Ticket | State | Commit | Notes |
|---|---|---|---|
| T-0001 | done | (early) | Solution skeleton, project graph per ADR 0001/0008 |
| T-0002 | done | (early) | `MakablesDbContext` + audit interceptor + soft-delete filter |
| T-0003 | done | (early) | MediatR + FluentValidation + Validation/UoW pipeline behaviors |
| T-0004 | done | (early) | `BusinessResult` / `Error` / `ErrorType` / `MakablesApiController` |
| T-0005 | done | (early) | `Money` value object + formatter; reviewer fix in T-0006 (ctor validation) |
| T-0006 | done | (early) | `Auditable` + `IClock` + `IIdGenerator` + `IUserSessionProvider` |
| T-0007 | done | (early) | `NumberingSequence` + three generators (`SELECT FOR UPDATE`) |
| T-0008 | done | (early) | `AddMakables{Infrastructure,Auth,Cors,Mediator,Clients,RateLimiting}` |
| T-0009 | done | `9f60d25` | Four Web hosts; first integration tests (`WebApplicationFactory`) |
| T-0010 | done | `29517be` | `Country` + `CountryConfiguration` + initial migration (CZ row) |
| T-0011 | done | `03f5991` | Outbox + AdminAuditLog + `AdminAuditPipelineBehavior` (BLOCKER fix folded into T-0013) |
| T-0012 | done | `47d4ec0` | API versioning + per-host `/openapi/v1.json` |
| T-0013 | done | `1dce32b` | NSwag config, generate script, CI parity check, manual-edits check; T-0011 BLOCKER fix |
| T-0014 | done | `8a9875e` + `209f732` | Serilog + OTel + Azure Monitor; reviewer fix folded 3 BLOCKERs |
| T-0015 | done | `500a0a9` | Frontend scaffold: `lib/runtime`, `lib/auth`, `lib/i18n`, route groups, JWT middleware; pre-pivot Supabase rip-out (ADR 0007 follow-through) |
| T-0016 | done | (this commit) | Bicep templates, GitHub Actions, Husky; T-0015 reviewer fix folded |

## Tests

- **Backend:** 109 unit + 36 integration = 145 tests, all passing.
- **Frontend:** typecheck clean, ESLint clean on T-0015 surface, `next build` succeeds. Unit-test infra (Jest/Vitest) deferred to a Phase-2 follow-up — the runtime helpers are exercised end-to-end as Phase-2 pages light up.
- **CI:** `.github/workflows/ci.yml` is the first run that exercises all of the above on a clean Ubuntu runner; first invocation pending the user's push.

## Reviewer-flow learnings

We ran a strict-gate reviewer-in-parallel pattern: each ticket merged to master, then a background reviewer compared the commit against ADRs and ticket scope. The reviewer caught three consequential rounds of findings this sprint:

- **T-0011 BLOCKER** — pipeline behavior registration order put `AdminAudit` outside `UnitOfWork`, so the audit row added after `next()` returned was never persisted. Folded into T-0013.
- **T-0014 BLOCKER × 3** — sampling, custom-meter registration, and sensitive-property redaction were missing from the observability wiring. Folded into a follow-up commit (`209f732`) before T-0015 started; bundled as one commit because the fixes interlock.
- **T-0015 BLOCKER × 2 + MAJOR × 4** — `apiFetch` cookie contract doc-drift, middleware matcher missing future route, `AbortSignal` composition bug, type-unsafe `_debugUrl` field, ADR 0022 drift. Folded into T-0016 because they share the same surface and the deploy infrastructure depends on stable runtime helpers.

The pattern works. Reviewer's marginal cost is low (≈ 1–2 min per commit) and catches things the dev pass misses.

## Carried follow-ups (not blocking the next sprint)

- T-0002 reviewer MINOR #1 — ADR 0013 stale claim about EF `Remove`.
- T-0010 reviewer 4 MINOR — VAT negative-guard, boundary tests, entity-config file split.
- T-0011 reviewer remaining (after BLOCKER fixed) — expand redaction list, pipeline e2e test, composite-PK assumption, catch-all exception suppression, entity-name ambiguity, `IAdminAuditableCommand` marker scope, "system" actor warning, dynamic-await pattern.
- T-0014 reviewer N-4 (`*Extensions` plural-class convention sweep).
- T-0015 reviewer M5 (no Jest infra) + MINOR follow-ups (text/route-group dedup, root layout TODO).
- T-0016 ops follow-ups in T-0134 (private endpoints, secret rotation playbook).

These live in the per-ticket review trails; PM picks them into the next available sprint.

## Push status

20 commits ahead of `origin/master`. **Never pushed** per the user's directive ("Leave local; you push when ready"). The user has the green light to push or to keep iterating; the working tree is clean and `master` is fast-forward-mergeable into `origin/master`.

## Definition of done

- [x] Every ticket commit builds clean
- [x] Backend tests all green (145)
- [x] Frontend typecheck + build green
- [x] INDEX.md state column reflects shipped state
- [x] Every reviewer BLOCKER closed; MAJORs either closed or moved to a tracked follow-up
- [x] ADRs updated where reality diverged (ADR 0022 amended in T-0016)
- [x] This status doc written
