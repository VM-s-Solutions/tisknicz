#!/usr/bin/env node
/**
 * Generate the TypeScript API clients from each backend host's
 * /openapi/v1.json via NSwag. Per ADR 0022.
 *
 * Prereqs:
 *   - npx nswag (resolved via @nswag/cli or the standalone package).
 *   - Each backend host must be running on its configured port. Without
 *     a host running, the corresponding regeneration is skipped with a
 *     warning so the script doesn't fail when only one host changed.
 *
 * Usage:
 *   npm run generate:api              # regenerate all four
 *   npm run generate:api -- --host customer
 *
 * Pipeline:
 *   1. Read nswag/config.json
 *   2. For each host: probe /openapi/v1.json; if reachable, run NSwag with
 *      that document → write to ../src/lib/api-client/<host>-api.v1.ts
 *   3. Recompute the spec hash; update src/lib/api-client/.spec-hashes.json
 *   4. Run prettier over the regenerated files for stable diffs
 */
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const frontendDir = resolve(here, '..');
const configPath = resolve(frontendDir, 'nswag/config.json');
const hashesPath = resolve(frontendDir, 'src/lib/api-client/.spec-hashes.json');

const args = process.argv.slice(2);
const hostFilter = (() => {
  const idx = args.indexOf('--host');
  return idx >= 0 ? args[idx + 1] : null;
})();

const config = JSON.parse(readFileSync(configPath, 'utf8'));
const documents = config.documentGenerator.fromDocuments;
// T-0046b Copilot review: this file's _comment describes the HASHES file
// (per-host SHA256 of the OpenAPI spec — CI uses it for the parity
// check). It is NOT the nswag/config.json _comment; the previous version
// of this script clobbered the hashes _comment with config's on every
// run. Keep the hashes _comment stable; only mutate per-host entries.
const HASHES_DEFAULT_COMMENT =
  'OpenAPI spec hashes per host. CI verifies the committed client matches the live spec from the backend. Per ADR 0022.';
const hashes = existsSync(hashesPath)
  ? JSON.parse(readFileSync(hashesPath, 'utf8'))
  : { _comment: HASHES_DEFAULT_COMMENT };
if (typeof hashes._comment !== 'string' || hashes._comment.length === 0) {
  hashes._comment = HASHES_DEFAULT_COMMENT;
}

let okCount = 0;
let skipCount = 0;

for (const doc of documents) {
  if (hostFilter && doc.host !== hostFilter) continue;

  const probe = await fetch(doc.url, { signal: AbortSignal.timeout(2000) })
    .catch(() => null);
  if (!probe || !probe.ok) {
    console.warn(`[skip] ${doc.host}: ${doc.url} not reachable. Start the host and re-run.`);
    skipCount++;
    continue;
  }

  const specText = await probe.text();
  const hash = createHash('sha256').update(specText).digest('hex');

  console.log(`[gen] ${doc.host} ${doc.url} → ${doc.output} (sha256 ${hash.slice(0, 12)}…)`);

  const result = spawnSync('npx', [
    'nswag', 'openapi2tsclient',
    `/input:${doc.url}`,
    `/output:${resolve(frontendDir, doc.output.replace(/^\.\.\//, ''))}`,
    `/className:${doc.className}`,
    '/template:Fetch',
    '/operationGenerationMode:MultipleClientsFromOperationId',
    '/withCredentials:true',
    '/generateClientInterfaces:true',
    '/typeScriptVersion:5.0',
  ], { stdio: 'inherit', shell: true });

  if (result.status !== 0) {
    console.error(`[fail] ${doc.host}: nswag exited with ${result.status}`);
    process.exit(result.status ?? 1);
  }

  // T-0049b: NSwag's Fetch template emits `FileParameter` references for
  // multipart endpoints (e.g. Maker host POST /products/{id}/images) but
  // doesn't include the interface declaration itself — the type is just
  // referenced unbound, which makes `tsc --noEmit` fail. Append the
  // canonical NSwag shape to the bottom of the generated file when the
  // identifier appears. The shape (`data: any; fileName: string`) is
  // baked into how the template constructs the FormData payload (see the
  // `content_.append("file", file.data, file.fileName ? file.fileName :
  // "file")` line in the generated client), so this declaration is the
  // contract — not an ad-hoc patch.
  const outputPath = resolve(frontendDir, doc.output.replace(/^\.\.\//, ''));
  const generated = readFileSync(outputPath, 'utf8');
  if (generated.includes('FileParameter') && !/(interface|type)\s+FileParameter\b/.test(generated)) {
    const appendix =
      '\n/** Multipart helper type referenced by NSwag-generated multipart\n' +
      ' * client methods. Injected by scripts/generate-api.mjs because the\n' +
      ' * Fetch template uses the identifier but does not emit the\n' +
      ' * declaration. T-0049b. */\n' +
      'export interface FileParameter { data: any; fileName: string; }\n';
    writeFileSync(outputPath, generated + appendix, 'utf8');
  }

  const specName = `${doc.host}-api.v1`;
  hashes[specName] = hash;
  okCount++;
}

writeFileSync(hashesPath, JSON.stringify(hashes, null, 2) + '\n', 'utf8');

console.log('');
console.log(`Done. ${okCount} regenerated, ${skipCount} skipped.`);
console.log('Commit the regenerated *.ts files together with .spec-hashes.json.');
