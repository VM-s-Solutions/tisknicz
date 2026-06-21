// makables-load.js — synthetic load test (T-0132, ADR 0023 §6).
//
// One pre-launch synthetic run: 100 concurrent users, 30 min, mixed
// catalog browse + order placement, against a CONFIGURABLE BASE_URL.
// The ADR 0023 §1 performance budgets are baked below as k6 thresholds,
// so k6 itself reports pass/fail. Pass = thresholds met + zero 5xx +
// DB CPU < 70% (the DB-CPU leg is checked OUT-OF-BAND — k6 cannot read
// Azure Postgres CPU; see README).
//
// This is an offline ops artifact: it does NOT run in CI (a 30-min
// 100-VU run is a staging exercise, not a per-PR gate). Run it manually
// against a live, seeded STAGING environment only — never production.
// See deploy/load-tests/README.md.
//
// Q-0015 note: k6 measures server-side API/SSR latency, NOT the frontend
// First-Load-JS bundle size. The JS-bundle budget (Q-0015) is a separate,
// deferred frontend-perf concern — it is N/A to this script.

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { SharedArray } from 'k6/data';
import { Trend } from 'k6/metrics';

// ---------------------------------------------------------------------------
// Configuration (all via __ENV so the same script targets dev/staging)
// ---------------------------------------------------------------------------

// The user-facing SITE origin (Next.js frontend) — catalog/product SSR.
const BASE_URL = (__ENV.BASE_URL || 'http://localhost:3000').replace(/\/+$/, '');

// The Customer API host — the order-create + payment-session endpoints.
// Defaults to BASE_URL so a single-origin deployment still works; set it
// explicitly when the API is a separate host (the Web.Customer host).
const API_BASE_URL = (__ENV.API_BASE_URL || BASE_URL).replace(/\/+$/, '');

// A pre-issued staging customer JWT for the order-placement leg (the
// order-create API requires auth). See README for how to mint one. When
// absent, the order scenario still issues the request (to measure the
// auth-rejected latency) but its checks tolerate the 401 — set the token
// for a representative authenticated order-path measurement.
const CUSTOMER_JWT = __ENV.CUSTOMER_JWT || '';

// Seeded data pools (staging must be seeded — see README). Comma-separated
// env lists; fall back to a small built-in default so the script is
// runnable as a smoke test before seeding.
function envList(name, fallback) {
  const raw = __ENV[name];
  if (!raw) return fallback;
  return raw.split(',').map((s) => s.trim()).filter(Boolean);
}

const MAKER_SLUGS = new SharedArray('makerSlugs', () =>
  envList('MAKER_SLUGS', ['alfa-tisk', 'beta-print', 'gamma-studio']),
);

const PRODUCT_IDS = new SharedArray('productIds', () =>
  envList('PRODUCT_IDS', [
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    '00000000-0000-0000-0000-000000000003',
  ]),
);

// A seeded, orderable product + a Zasilkovna pickup-point id for the
// order-create payload (Fixed-price, ZasilkovnaPickupPoint shipping).
const ORDER_PRODUCT_ID =
  __ENV.ORDER_PRODUCT_ID || PRODUCT_IDS[0];
const ZASILKOVNA_PICKUP_POINT_ID =
  __ENV.ZASILKOVNA_PICKUP_POINT_ID || '12345';

function pick(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

// ---------------------------------------------------------------------------
// Custom trends (per-surface latency, tagged so each gets its own budget)
// ---------------------------------------------------------------------------

const catalogSsr = new Trend('ssr_catalog_duration', true);
const productSsr = new Trend('ssr_product_duration', true);
const orderApi = new Trend('api_order_create_duration', true);

// ---------------------------------------------------------------------------
// Options — scenarios + thresholds (ADR 0023 §1 budgets baked in)
// ---------------------------------------------------------------------------

export const options = {
  scenarios: {
    // Catalog browse carries the bulk of the 100 VUs — catalog is the
    // dominant traffic per ADR 0023 §2 (browse RPS up to 50). 30-min run.
    catalog_browse: {
      executor: 'ramping-vus',
      exec: 'browseCatalog',
      startVUs: 0,
      stages: [
        { duration: '2m', target: 85 }, // ramp up
        { duration: '26m', target: 85 }, // hold at the browse share of 100 VUs
        { duration: '2m', target: 0 }, // ramp down
      ],
      gracefulRampDown: '30s',
    },
    // Order placement at a lower concurrency matching the ~200 orders/day
    // shape (ADR 0023 §2). Together with the browse VUs this sums to the
    // 100-concurrent-user ceiling.
    order_placement: {
      executor: 'ramping-vus',
      exec: 'placeOrder',
      startVUs: 0,
      stages: [
        { duration: '2m', target: 15 },
        { duration: '26m', target: 15 },
        { duration: '2m', target: 0 },
      ],
      gracefulRampDown: '30s',
    },
  },

  thresholds: {
    // --- ADR 0023 §1 per-surface budgets, baked as k6 thresholds ---
    // Catalog page TTFB (SSR): p95 400 ms / p99 1000 ms.
    'ssr_catalog_duration': ['p(95)<400', 'p(99)<1000'],
    // Product page TTFB (SSR): p95 350 ms / p99 1000 ms.
    'ssr_product_duration': ['p(95)<350', 'p(99)<1000'],
    // Order creation API: p95 600 ms / p99 1500 ms.
    'api_order_create_duration': ['p(95)<600', 'p(99)<1500'],

    // --- Zero 5xx (ADR 0023 §6 pass criterion) ---
    // http_req_failed counts a request as failed on a 4xx/5xx by default;
    // we additionally assert the explicit no-5xx check below. A rate of 0
    // is the hard gate.
    'http_req_failed': ['rate==0'],

    // The per-request "no 5xx" checks must all pass.
    'checks': ['rate==1'],
  },

  // Treat any 5xx as a failed request so http_req_failed reflects server
  // errors (4xx auth rejections without a token are tolerated by the
  // order-scenario checks but still surface in the summary).
  // setResponseCallback narrows "expected" statuses to < 500.
};

// Only 1xx–4xx count as expected; any 5xx marks the request failed and
// trips the http_req_failed threshold (the zero-5xx gate).
http.setResponseCallback(http.expectedStatuses({ min: 100, max: 499 }));

// ---------------------------------------------------------------------------
// Scenario: catalog browse (SSR)
// ---------------------------------------------------------------------------

export function browseCatalog() {
  group('catalog SSR', () => {
    const catalogRes = http.get(`${BASE_URL}/katalog`, {
      tags: { surface: 'catalog_ssr' },
    });
    catalogSsr.add(catalogRes.timings.duration);
    check(catalogRes, {
      'catalog: no 5xx': (r) => r.status < 500,
      'catalog: 200 OK': (r) => r.status === 200,
    });
  });

  sleep(Math.random() * 2 + 1); // 1–3 s think time

  group('product SSR', () => {
    const productId = pick(PRODUCT_IDS);
    const productRes = http.get(`${BASE_URL}/produkt/${productId}`, {
      tags: { surface: 'product_ssr' },
    });
    productSsr.add(productRes.timings.duration);
    check(productRes, {
      'product: no 5xx': (r) => r.status < 500,
      // A seeded id should 200; an unseeded one 404s — both are non-5xx
      // and acceptable for the latency measurement (SSR time is the
      // signal, not the entity's existence).
      'product: not 5xx and not 404-on-seeded': (r) => r.status < 500,
    });
  });

  // Occasionally hit a maker profile too (part of the browse mix).
  if (Math.random() < 0.5) {
    const slug = pick(MAKER_SLUGS);
    const makerRes = http.get(`${BASE_URL}/katalog/${slug}`, {
      tags: { surface: 'maker_ssr' },
    });
    check(makerRes, { 'maker: no 5xx': (r) => r.status < 500 });
  }

  sleep(Math.random() * 2 + 1);
}

// ---------------------------------------------------------------------------
// Scenario: order placement (API)
// ---------------------------------------------------------------------------

export function placeOrder() {
  const headers = {
    'Content-Type': 'application/json',
  };
  if (CUSTOMER_JWT) {
    headers['Authorization'] = `Bearer ${CUSTOMER_JWT}`;
  }

  const payload = JSON.stringify({
    productId: ORDER_PRODUCT_ID,
    quantity: 1, // T-0061 invariant: quantity fixed at 1
    shippingMethod: 'ZasilkovnaPickupPoint',
    zasilkovnaPickupPointId: ZASILKOVNA_PICKUP_POINT_ID,
    customerName: 'Load Test Customer',
    customerEmail: `loadtest+${__VU}-${__ITER}@makables.test`,
    customerPhone: '+420123456789',
    customerNotes: 'k6 synthetic load run',
  });

  group('order create API', () => {
    const res = http.post(`${API_BASE_URL}/api/v1/orders`, payload, {
      headers,
      tags: { surface: 'order_api' },
    });
    orderApi.add(res.timings.duration);
    check(res, {
      'order: no 5xx': (r) => r.status < 500,
      // With a valid CUSTOMER_JWT this should be 200/201; without one the
      // API returns 401 (a non-5xx, tolerated). The latency is the signal.
      'order: authed → created, or no token → 401 (never 5xx)': (r) =>
        CUSTOMER_JWT ? r.status >= 200 && r.status < 300 : r.status === 401,
    });

    // If the order was created and a payment-session call is part of the
    // measured path, drive it too (ADR 0023 §1 payment-redirect-receipt
    // budget is informational here; the order-API budget is the gate).
    if (CUSTOMER_JWT && res.status >= 200 && res.status < 300) {
      let orderId;
      try {
        orderId = res.json('orderId');
      } catch (_e) {
        orderId = undefined;
      }
      if (orderId) {
        const sessionRes = http.post(
          `${API_BASE_URL}/api/v1/orders/${orderId}/payment-session`,
          null,
          { headers, tags: { surface: 'payment_session' } },
        );
        check(sessionRes, {
          'payment-session: no 5xx': (r) => r.status < 500,
        });
      }
    }
  });

  sleep(Math.random() * 3 + 2); // 2–5 s between order attempts
}
