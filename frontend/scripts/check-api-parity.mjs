#!/usr/bin/env node
/**
 * CI parity check per ADR 0022. Verifies the committed
 * `src/lib/api-client/.spec-hashes.json` matches the live
 * `/openapi/v1.json` from each backend host.
 *
 * Run in CI after the backend hosts are started. Exits non-zero (with a
 * clear "regenerate the client" message) if any host's spec hash differs
 * from the committed value.
 *
 * Locally:
 *   - To regenerate: `npm run generate:api`
 *   - To check: `npm run check:api`
 */
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { canonicalJsonHash } from './lib/canonical-json.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const frontendDir = resolve(here, '..');
const configPath = resolve(frontendDir, 'nswag/config.json');
const hashesPath = resolve(frontendDir, 'src/lib/api-client/.spec-hashes.json');

const config = JSON.parse(readFileSync(configPath, 'utf8'));
const documents = config.documentGenerator.fromDocuments;
const hashes = existsSync(hashesPath) ? JSON.parse(readFileSync(hashesPath, 'utf8')) : {};

let drift = 0;
let missing = 0;

for (const doc of documents) {
  const specName = `${doc.host}-api.v1`;
  const expected = hashes[specName];

  const probe = await fetch(doc.url, { signal: AbortSignal.timeout(3000) })
    .catch(() => null);

  if (!probe || !probe.ok) {
    console.error(`[fail] ${specName}: ${doc.url} not reachable in CI.`);
    missing++;
    continue;
  }

  const specText = await probe.text();
  // Canonicalized (key-sorted) hash — see lib/canonical-json.mjs for why.
  const actual = canonicalJsonHash(createHash, specText);

  if (expected === null || expected === undefined) {
    console.warn(`[skip] ${specName}: no committed hash yet (set after first generation).`);
    continue;
  }

  if (actual !== expected) {
    console.error(`[drift] ${specName}: committed ${expected.slice(0, 12)}… but live spec is ${actual.slice(0, 12)}…`);
    console.error(`        If the API contract genuinely changed, run \`npm run generate:api -- --host ${doc.host}\``);
    console.error(`        and commit the regenerated files.`);
    console.error(`        If you added an ops/liveness endpoint (health, readiness, a root probe),`);
    console.error(`        do NOT regenerate — that bakes it into the client contract. Add`);
    console.error(`        .ExcludeFromDescription() to the MapGet/MapPost call instead, so only`);
    console.error(`        /api/ routes are described. OpsEndpointsExcludedFromContractTests pins this.`);
    drift++;
  } else {
    console.log(`[ok] ${specName}: ${actual.slice(0, 12)}…`);
  }
}

if (drift > 0 || missing > 0) {
  console.error(`\nParity check failed: ${drift} drifted spec(s), ${missing} unreachable host(s).`);
  process.exit(1);
}

console.log('\nAll API client hashes match the live OpenAPI specs.');
