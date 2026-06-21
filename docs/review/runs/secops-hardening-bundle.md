# secops-hardening-bundle — Reviewer FINAL verdict

> Final PR-open pass for `feat/secops-hardening-bundle` (T-0136 + T-0137).
> Four-gate adversarial fan-out: Reviewer (Gates 1-7), SecOps (Gate 3),
> Architect (Gate 4 — two new seams), Optimizer (Gate 8 — global limiter +
> read-audit write on a path). All folds below are committed in this PR.

**Commits reviewed:** `b4d0f61` (grooming) + `3d0710a` (impl) + the fold commit
(this doc + the Gate folds). Diff vs `master`: backend rate-limit mount +
read-audit seam + 3 wired admin controllers + tests + ticket/ADR/checklist docs.

## Bundle summary

The last two pre-launch security gaps, both code-only, no migration, no contract change:

- **T-0136 (Q-0011) — rate-limit mount.** The per-audience "default" fixed-window
  policy (cust 100 / maker 60 / admin 30 / public 60 per min) was defined but
  mounted nowhere — every endpoint except two partitioned ones was unlimited,
  including the 14 anonymous auth endpoints + `PostMessage`. Now mounted as the
  per-host `GlobalLimiter` (per-`sub` / per-IP partition) + a tight per-IP `auth`
  policy (10/min) on `AuthController`. `OnRejected` emits `Retry-After`; 429 is a
  raw middleware status (no `BusinessErrorMessage`, no i18n key).
- **T-0137 (Q-0028) — admin PII-read audit.** Privileged admin reads of customer
  PII (invoice PDF, payout CSV, order detail) now write an `admin_audit_log` row
  via a new own-context `IAdminReadAuditWriter` (`IDbContextFactory`, the T-0032
  ARES-cache precedent) — success-path only, `beforeJson=afterJson=null`. List
  reads stay un-audited.

## Verdict: APPROVE (with folds, all applied in-PR)

| Gate | Reviewer | Verdict |
|---|---|---|
| 1-7 | reviewer | **APPROVE** — clean; 2 optional nits (now addressed in status logs) |
| 3 | secops | **PASS-WITH-FOLLOWUPS** — no diff-blocker; 1 HIGH ops-gap + 1 MEDIUM tuning folded |
| 4 | architect | **APPROVE** after the required ADR 0014 amendment (folded) |
| 8 | optimizer | **PASS-WITH-FOLLOWUPS** — no budget breach; 1 MEDIUM v1.1 memory note deferred |

## Folds applied in this PR

1. **(Architect, required) ADR 0014 amendment.** The code cited "per ADR 0014"
   for read-auditing, but ADR 0014 said "reads not audited" — a traceability
   contradiction. Added an *Amendment — 2026-06-21* subsection recording the
   narrow 3-read PII-disclosure carve-out + the own-context-writer mechanism +
   the fail-closed intent + a reviewer-checklist line. Fixed the two mis-citing
   code comments (`IAdminReadAuditWriter`, `AdminQueriesController`).
2. **(SecOps, MEDIUM) Auth-policy re-scope.** `refresh` + `logout` now carry
   `[DisableRateLimiting]` — `refresh` is machine-triggered (frontend auto-call
   on 401) and cookie-bearing, `logout` must never fail-closed. They fall under
   the global per-host envelope instead of the tight 10/min auth bucket, so a
   shared-NAT office / multi-tab session can't lock itself out. New integration
   fact `POST_auth_refresh_is_excluded_from_the_tight_auth_bucket` (live, passes).
3. **(SecOps, HIGH ops-gap) ForwardedHeaders prerequisite.** Corrected the
   misleading "X-Forwarded-For-aware" doc — the partition uses the RAW connection
   IP, which is correct in the current direct-App-Service deploy (no Front Door /
   App Gateway / WAF in bicep, verified). Added a BLOCKING-if-proxy-introduced
   launch-checklist line: any reverse proxy MUST land `UseForwardedHeaders`
   (restricted `KnownProxies`) + a regression test in the same change.

## Confirmed-clean (adversarially verified, REFUTED as issues)

- **304 / 404 / 409 not audited** — correct: a 304 returns no body (no
  disclosure); 404/409 are not successful reads. Pinned by unit + integration
  `Received(0)` / zero-row assertions.
- **Audit fail-closed** — the read-audit is `await`ed before the PII streams,
  no swallowing try/catch → an audit-DB failure 500s and no PII leaks. Correct
  for a forensic trail; matches the command-audit posture. Marked deliberate.
- **No PII in the audit row** — `target_id` points at the record; before/after
  null; no email/name/bank data copied in. Append-only table (DB trigger).
- **GlobalLimiter composition** — endpoint policy AND global limiter both apply
  (intended ASP.NET semantics); no double-limit bug on the two existing policies.
- **No country branching, no SaveChanges-in-handler, no inline error strings,
  no contract/NSwag change, T8/T9 hard checks green.**

## Deferred follow-ups (logged, NOT in this PR)

- **Q-0034** — rate-limit v1.1: config-bind the limit pairs + a distributed
  (Redis) partition store when the host scales past one instance. In-memory
  per-instance is adequate for single-region MVP; idle partitions are reclaimed
  by the AutoReplenishment sweep (bounded ~1 window).
- **Q-0035** — log the own-context-side-effect-writer pattern in
  `recurring-findings.md` at count 2 (ARES + read-audit); promote to a
  patterns.md §A.N entry at the third occurrence. Optional `AuditActionCodes`
  constants class if the code set grows.

## Test evidence

- Full solution builds clean (0 warnings, `TreatWarningsAsErrors` on).
- Unit suite: **1745 passed / 0 failed**.
- Integration (live Postgres): rate-limit **4/4** (login 429+Retry-After, refresh
  exclusion, GlobalLimiter registered, policy-name pinned); read-audit **7 facts**
  (each 200 writes one correctly-shaped row; 404/304/409 write zero).
- `node scripts/check-consistency.mjs` exits **0**.

**Sign-off: APPROVE — ship.** All gate folds applied; no open BLOCKERs.
