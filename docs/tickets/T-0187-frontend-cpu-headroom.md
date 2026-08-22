---
id: T-0187
title: Give the Next.js frontend real CPU on the shared App Service Plan
status: in_review
size: S
owner: claude
created: 2026-08-22
updated: 2026-08-22
depends_on: []
blocks: []
user_stories: []
adrs: [0023]
phase: 8
manual_steps: [deploy-trigger]
security_touching: false
layers: [frontend, optimizer, secops]
---

# T-0187 — Give the Next.js frontend real CPU on the shared App Service Plan

## Context

Operator report: *"aplikace v azure je hrozne pomala."* Black-box measurement
against `web-makables-weu-dev` on 2026-08-22 puts the cost squarely on the
frontend Node process, not on the backend or the database:

| What | conc=1 | conc=16 |
|---|---|---|
| `/vop` (SSR, **zero** backend calls) | 0.18 s | 0.62 – 1.13 s |
| `/katalog` (SSR + 2 backend calls) | 0.68 s | 1.79 – 3.66 s |
| `/health` on all four .NET hosts | 0.025 s | — |
| public API direct vs through `/api-proxy` | — | 0.15–0.28 s vs 0.26–0.54 s |

A page that touches no backend degrading 6x under load is Node CPU by
elimination — not the network, not Postgres, not a handler. The cause is the
topology recorded in ADR 0023 §7: **six always-on runtimes share one 2-vCPU
Linux plan** (four multi-threaded .NET hosts + Azure Functions + this), and
`output: 'standalone'` runs the frontend as **one process on one JS thread**
doing SSR *and* the T-0153 `/api-proxy` rewrite *and* `next/image`.

So the frontend brings a single runnable thread to a fight against five
multi-threaded neighbours. This ticket takes the two fixes that cost nothing
and need no plan resize; the plan split itself stays an operator decision.

## Scope

- `frontend/deploy/cluster.js` — new App Service entry point. Forks
  `WEB_CLUSTER_WORKERS` copies of the standalone `server.js` behind one shared
  listening socket (Node `cluster`, round-robin accept), with respawn, a
  crash-loop guard, and SIGTERM forwarding.
- `infra/bicep/modules/web-app.bicep` — `appCommandLine: 'node cluster.js'`,
  new `webClusterWorkers` param (default 2 = the plan's vCPU count) surfaced as
  the `WEB_CLUSTER_WORKERS` app setting so the count is tunable in the portal
  without a redeploy.
- Both deploy workflows — copy `deploy/cluster.js` into the standalone package
  (it is not part of the traced output) and `test -f` it so a missing copy
  fails the deploy instead of producing a site that never binds its port.
- `frontend/next.config.ts` — drop `image/avif`; serve WebP only.
- Tests pinning both decisions, plus the eslint override that lets the CJS
  bootstrap file keep `require()`.

## Alternatives Considered

- **Move the frontend to its own App Service Plan** — the correct fix and still
  the recommended next step, but it costs money every month, so it is the
  operator's call, not a silent PR. Left in "Zbývá" below.
- **Scale the shared plan B2 → B3 (2 → 4 vCPU)** — one-line
  `appServicePlanSku` change that helps all six runtimes; same objection: it
  doubles the plan bill.
- **Move Azure Functions off the shared plan onto Flex Consumption** — frees a
  whole always-on runtime and would likely *reduce* cost, but the Functions app
  is container-deployed (`DOTNET-ISOLATED|10.0`, Docker per ADR 0007), so the
  hosting-model switch is a real migration with its own deploy risk. Not a
  slice to bundle into a perf fix.
- **Keep AVIF and rely on the disk cache** — the container's
  `.next/cache/images` is wiped on every deploy, and this repo deploys often,
  so the encode cost is paid again and again.

## Out of scope

- Any change to the App Service Plan SKU, count, or the Functions hosting model.
- The `/api-proxy` hop itself (T-0153 follow-up: a shared parent domain would
  remove it, and with it a whole proxy leg on every browser API call).
- Argon2id cost — already cut to the OWASP configuration in `643430e`.

## Acceptance criteria

- **AC-1** Given the assembled deploy package, when App Service runs
  `node cluster.js`, then N worker processes serve on the single `PORT` and
  every public route answers 200.
- **AC-2** Given a worker is killed, when it exits unexpectedly, then the
  primary respawns it and the site keeps serving without a container restart.
- **AC-3** Given App Service sends SIGTERM, when the primary receives it, then
  every worker is stopped and no orphan process survives.
- **AC-4** Given a build whose workers die instantly, when the exits exceed the
  guard threshold, then the primary exits rather than masking it behind a
  "Running" site that 502s.
- **AC-5** Given a browser advertising `Accept: image/avif`, when it requests an
  optimized image, then the optimizer answers `image/webp`.
- **AC-6** Given either deploy workflow, when `deploy/cluster.js` is missing,
  then the assemble step fails.

## Technical notes

Nothing in the app is per-process stateful — sessions are HttpOnly cookies
validated by the .NET hosts (ADR 0012) and Next's in-memory response cache is a
cache — so a request may land on any worker.

Worker count is capped at 4 in code and 8 by configuration: past the core count
the workers only context-switch, and each costs ~110 MB RSS (measured) of the
plan's shared 3.5 GB.

## Files touched (expected)

- `frontend/deploy/cluster.js` (new)
- `frontend/next.config.ts`
- `frontend/eslint.config.mjs`
- `frontend/src/__tests__/deploy-cluster.test.ts` (new)
- `frontend/src/__tests__/next-config-image-formats.test.ts` (new)
- `infra/bicep/modules/web-app.bicep`
- `.github/workflows/deploy-staging.yml`
- `.github/workflows/deploy-production.yml`
