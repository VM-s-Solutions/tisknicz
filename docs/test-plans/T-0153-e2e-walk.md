# T-0153 — End-to-end walk evidence (dev)

Per-step evidence log for the core marketplace loop on the **dev**
environment (`web-makables-weu-dev.azurewebsites.net` +
`app-makables-*-weu-dev` API hosts). Row template follows the T-0135
smoke-checklist convention. Update in place as the walk progresses;
every ❌ must end as a fix commit or a filed ticket (T-0153 AC-5 —
zero silent skips).

**Legend:** ✅ pass · 🟡 pending (not yet run / blocked on a manual step) ·
❌ fail (link the fix/ticket) · ⛔ blocked (environment/ops, needs the operator)

> **⛔ 2026-07-20 — walk paused: dev App Services are STOPPED.** All five
> hosts return HTTP 403 with the Azure *"Web App — Unavailable"* stopped-site
> page (a crash would be 503; an IP block a different page). No `schedule:`/
> cron exists in either deploy workflow, so our pipeline did not stop them —
> most likely a manual weekend stop or an Azure spending-cap on this personal
> "Azure subscription 1" (a capped/disabled sub stops all App Services at
> once). **Restarting needs Azure access the agent does not have** (`az`
> refresh token expired 2026-07-17, and there is no start-apps workflow).
> **Operator action:** `az login` then `az webapp start` on the six dev apps
> (`web-makables-weu-dev`, `app-makables-{customer,maker,admin,public}-weu-dev`,
> `func-makables-weu-dev`), or Start them from the portal; then a fresh
> `Deploy → dev` (or just re-run the failed maker backend job). Phase 0/1
> results below stand from 2026-07-17 when the apps were up.

## Phase 0 — Environment (T-0153 AC-1)

| # | Step | Result | Evidence (2026-07-17) |
|---|---|---|---|
| 0.1 | All five App Services answer | ✅ | `GET /` → 200 on web + customer + maker + admin + public hosts (curl, 0.15–1.2 s) |
| 0.2 | Public catalog API answers with real JSON | ✅ | `GET /api/v1/catalog/makers?page=1&pageSize=2` → `{"items":[],"totalCount":0,…}` (empty catalog is the expected pre-walk state) |
| 0.3 | Deploy pipeline green end-to-end | ✅ | "Deploy → dev" run `29593679668` (2026-07-17): Bicep + migrations + 4 backends + Functions + frontend + `/health` smoke all `success` |
| 0.4 | Stale-doc reconciliation | ✅ | dopady §4 🔴 1 ("backend down", `makables-dev-*` hostnames) is obsolete — hosts renamed to CAF convention. Recorded in T-0153 status log |
| 0.5 | Hosts up **now** (2026-07-20) | ⛔ | All five → 403 stopped-site page. See the ⛔ banner above — operator must start the apps before the walk resumes |
| 0.6 | Deploy-race hardening (walk-surfaced defect) | ✅ | The 2026-07-17T16:15 `Deploy → dev` failed: docs PR #97 re-ran Bicep → App Services bounced → maker backend zipdeploy hit the OneDeploy *"SCM container restart"* race. Fix: `paths-ignore` (`**/*.md`, `docs/**`, `agents/**`, `.claude/**`) on the dev deploy trigger so docs-only merges no longer redeploy the stack (also saves cost on the capped sub). Prod is manual-dispatch only — unaffected |

## Phase 1 — Same-origin proxy + auth seam (T-0153 AC-6, PR #96)

| # | Step | Result | Evidence (2026-07-17) |
|---|---|---|---|
| 1.1 | Catalog GET transits the proxy | ✅ | `GET {web}/api-proxy/public/api/v1/catalog/makers` → same JSON as the direct host |
| 1.2 | POST with JSON body transits the proxy | ✅ | `POST {web}/api-proxy/customer/api/v1/auth/login` (bogus creds) → 400 `auth.invalidCredentials`, `x-correlation-id` echoed, served by Kestrel via the Next rewrite |
| 1.3 | Frontend SSR renders on dev | ✅ | Homepage HTML contains the hero + navbar strings |
| 1.4 | Customer registration through the proxy | ✅ | `POST …/auth/register` → 200 `{"userId":"01KXRDNMC3EPA253PEZ841P6AH"}` — test account `vitchvoj+t0153@gmail.com` (credentials shared out-of-band; dev-only) |
| 1.5 | Email-confirmation gate enforced | ✅ | Same-credentials login pre-confirmation → 400 `auth.emailNotConfirmed` |
| 1.6 | Confirmation email arrives + link works | 🟡 | Outbox → SendGrid → operator inbox; **manual: click the link in the `vitchvoj+t0153@gmail.com` confirmation email**. If nothing arrives, check `/dashboard/admin/outbox` for a parked event |
| 1.7 | Login sets first-party session cookies; navbar shows "Můj účet"; dashboard reachable | 🟡 | Blocked on 1.6. Expected: `Set-Cookie makables_access_customer` (no `Domain` → host-only on the web origin), navbar account menu (T-0152), `/dashboard/zakaznik/objednavky` renders |
| 1.8 | Google OAuth through the proxy | 🟡 | Blocked on the manual Google-console redirect-URI allowlist entry (see T-0153 status log) |
| 1.9 | Session persists past the access-token lifetime | ❌→fix | Operator report 2026-07-23: "does not hold logged in state". Root cause: 15-min access JWT/cookie (`JwtOptions.AccessTokenLifetime`) and **zero** frontend callers of `/api/v1/auth/refresh` — the T-0035 helper was never wired. Fix = [T-0154](../tickets/T-0154-session-refresh.md): middleware session refresh (all pages, de-duped, request-patching) + apiFetch 401 → refresh → retry-once. Re-verify on dev after merge+deploy: log in, wait >15 min (or delete the access cookie in devtools), reload → still logged in |
| 1.10 | Authenticated pages actually work (profile, orders) | ❌→fix | Operator report 2026-07-23: logged in, but Profile bounces to login and Orders says "please log in". Root cause: `AddMakablesAuth` wired stock JwtBearer (Authorization-header-only) while the session JWT lives in an HttpOnly cookie the browser can't convert — **every `[Authorize]` endpoint 401'd for every browser session since T-0027**; test rigs pass Bearer headers so the suite never saw it. Fix = [T-0156](../tickets/T-0156-cookie-jwt-bridge.md) (OnMessageReceived cookie→JWT bridge, header precedence, audience-order probe). Re-verify after merge+deploy: log in on dev → open `/dashboard/zakaznik/profile` + `objednavky` → both render |

## Phase 2 — Maker journey (AC-2)

| # | Step | Result | Evidence |
|---|---|---|---|
| 2.1 | Maker registration via `/register/maker` (real IČO → ARES prefill) | 🟡 | Needs a real IČO the operator controls — manual |
| 2.2 | Maker email confirm + admin verification | 🟡 | Admin host/dashboard step |
| 2.3 | Product created with image + price | 🟡 | |
| 2.4 | Product appears on `/katalog` + `/produkt/[id]` | 🟡 | |

## Phase 3 — Customer order → payment → fulfillment (AC-3, AC-4)

| # | Step | Result | Evidence |
|---|---|---|---|
| 3.1 | Order placement (`/objednavka?productId=`, Zásilkovna widget) | 🟡 | |
| 3.2 | Comgate sandbox payment → webhook drives `Paid` | 🟡 | Verify merchant-portal return URLs are set for dev first (T-0085 manual step) |
| 3.3 | Confirmation page + customer order list/detail | 🟡 | |
| 3.4 | Maker accepts → ships (label PDF) → customer confirms delivery | 🟡 | |
| 3.5 | Invoice PDFs download (customer + maker fee at payout) | 🟡 | |
| 3.6 | Lifecycle emails arrive | 🟡 | |
