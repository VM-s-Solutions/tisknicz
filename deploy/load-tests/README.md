# Makables load tests (k6) — T-0132

The synthetic pre-launch load run ADR 0023 §6 mandates:

> One synthetic load run before launch: **100 concurrent users, mixed
> catalog browse + order placement, 30 min**, k6 script committed to
> `deploy/load-tests/`. Pass criteria: **p95 latency under budget; zero
> 5xx; database CPU under 70%.**

`makables-load.js` implements that run and bakes the ADR 0023 §1
performance budgets as k6 **thresholds**, so k6 itself reports pass/fail.

> **This RUN is a pre-launch, STAGING-ONLY exercise. Never run it against
> production.** It is not wired into CI — a 30-min 100-VU run is not a
> per-PR gate. The script + this README ship now; the execution is the
> gated manual step (Ops/QA, against a live seeded staging env).

## What it measures (and what it does NOT)

| Surface (tagged metric) | ADR 0023 §1 budget (baked threshold) |
|---|---|
| Catalog page SSR (`GET /katalog`) — `ssr_catalog_duration` | p95 < **400 ms**, p99 < **1000 ms** |
| Product page SSR (`GET /produkt/{id}`) — `ssr_product_duration` | p95 < **350 ms**, p99 < **1000 ms** |
| Order creation API (`POST /api/v1/orders`) — `api_order_create_duration` | p95 < **600 ms**, p99 < **1500 ms** |
| Zero 5xx (all requests) — `http_req_failed` | `rate == 0` |

**Q-0015 is NOT this.** k6 measures **server-side API/SSR latency**. The
frontend **First-Load-JS bundle budget** (Q-0015) is a different axis —
client-side JS weight, which k6 has no view of. Q-0015 stays a separate,
deferred frontend-perf concern; do not read "we ran k6" as "we have a JS
bundle budget."

**DB CPU < 70% is checked OUT-OF-BAND.** k6 cannot read Azure Postgres
CPU. During the run, watch the **Azure Portal → Postgres Flexible Server
→ Monitoring → Metrics → CPU percent** blade and confirm it stays under
70%. Record that verdict alongside the k6 threshold output.

## Install k6

k6 is a standalone Go binary (it is **not** an npm package — do not
`npm install k6`).

- **macOS:** `brew install k6`
- **Windows:** `winget install k6 --source winget` (or `choco install k6`)
- **Linux:** see <https://grafana.com/docs/k6/latest/set-up/install-k6/>
- **Docker:** `docker run --rm -i grafana/k6 run - < makables-load.js`

Verify: `k6 version`.

## Seed staging (precondition)

The catalog/product/order requests must hit **real seeded rows**, or the
SSR latency is measured against 404 pages and the order leg is rejected:

1. Deploy the full stack to staging (frontend + the four API hosts +
   Postgres) per `deploy/` / the deployment runbook.
2. Seed **published makers** (so `/katalog` lists rows and `/katalog/{slug}`
   resolves) and **active, orderable products** (so `/produkt/{id}` 200s
   and the order-create payload references a real product).
3. Collect the seeded **maker slugs**, **product ids**, and one
   **orderable product id** for the order leg.
4. Seed a **test customer** and mint a staging **JWT** for the
   order-placement leg (the order-create API requires auth). Use the
   staging auth flow (login → copy the access token) or an admin-issued
   token. Without a JWT the order scenario still runs but measures the
   401-rejection latency (its checks tolerate the 401) — set the token
   for a representative authenticated order-path number.
5. Have a **Zasilkovna pickup-point id** for the shipping field (any
   valid staging pickup-point id; the payload uses
   `shippingMethod: "ZasilkovnaPickupPoint"`).

## Configure (env vars)

| Env var | Required | Purpose | Default |
|---|---|---|---|
| `BASE_URL` | yes | Frontend (SSR) origin — catalog/product pages | `http://localhost:3000` |
| `API_BASE_URL` | if API is a separate host | Customer API host — order-create + payment-session | falls back to `BASE_URL` |
| `CUSTOMER_JWT` | for a real order measurement | Bearer token for the order-create leg | empty (order leg measures 401) |
| `MAKER_SLUGS` | recommended | Comma-separated seeded maker slugs | built-in stub list |
| `PRODUCT_IDS` | recommended | Comma-separated seeded product ids | built-in stub list |
| `ORDER_PRODUCT_ID` | recommended | A seeded **orderable** product id for the order leg | first of `PRODUCT_IDS` |
| `ZASILKOVNA_PICKUP_POINT_ID` | recommended | Pickup-point id for the order payload | `12345` |

## Run

```bash
# Against staging, with seeded pools + a customer JWT:
k6 run \
  -e BASE_URL=https://staging.makables.cz \
  -e API_BASE_URL=https://api-customer.staging.makables.cz \
  -e CUSTOMER_JWT="$STAGING_CUSTOMER_JWT" \
  -e MAKER_SLUGS="alfa-tisk,beta-print,gamma-studio" \
  -e PRODUCT_IDS="<id1>,<id2>,<id3>" \
  -e ORDER_PRODUCT_ID="<orderable-id>" \
  -e ZASILKOVNA_PICKUP_POINT_ID="<pickup-id>" \
  makables-load.js
```

A shorter smoke run (sanity-check connectivity + seeding before the full
30-min run) — override the stage durations from the CLI:

```bash
k6 run --stage 30s:10 --stage 1m:10 --stage 30s:0 -e BASE_URL=https://staging.makables.cz makables-load.js
```

> The full run takes **30 minutes** (2m ramp + 26m hold + 2m ramp-down)
> and drives **~100 concurrent VUs** (85 browse + 15 order).

## Read the output

At the end k6 prints a summary. The run **PASSES** iff:

1. Every `✓ THRESHOLD` line is green — specifically:
   - `ssr_catalog_duration … p(95)<400, p(99)<1000` ✓
   - `ssr_product_duration … p(95)<350, p(99)<1000` ✓
   - `api_order_create_duration … p(95)<600, p(99)<1500` ✓
   - `http_req_failed … rate==0` ✓ (**zero 5xx**)
   - `checks … rate==1` ✓ (every per-request check passed)
2. **AND** the out-of-band **Postgres CPU stayed < 70%** during the run
   (Azure metrics blade — k6 cannot see this).

A red threshold (or `http_req_failed` > 0, or DB CPU ≥ 70%) is a
**FAIL**. Record the summary + the DB-CPU verdict in the sprint status /
launch checklist. A budget MISS becomes a **perf follow-up ticket** per
ADR 0023 §1 — it is not a T-0132 merge blocker (the script ships
regardless; the verdict is the gated pre-launch finding).

## Notes

- The script tags each request surface distinctly so each gets its own
  per-surface budget; the custom `Trend`s (`ssr_catalog_duration`, …) are
  what the thresholds assert on.
- `http.setResponseCallback(expectedStatuses(100–499))` makes any **5xx**
  count as a failed request, so `http_req_failed: rate==0` is the
  zero-5xx gate; each request additionally carries an explicit
  `no 5xx: status < 500` check.
- Think-time `sleep()`s (1–5 s) keep the per-VU request rate realistic
  rather than hammering at zero delay.
- Staging only. Never production. Read-only load generation — if the run
  melts staging, stop it (Ctrl-C); staging is a seeded scratch env, no
  data rollback needed.
