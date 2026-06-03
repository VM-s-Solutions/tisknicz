#!/usr/bin/env node
/**
 * Pre-commit hook per ADR 0022. Rejects commits that modify any
 * `src/lib/api-client/*-api.v1.ts` file without ALSO staging either
 * `src/lib/api-client/.spec-hashes.json` (the normal case — the
 * generator updated the hash) OR `scripts/generate-api.mjs` (the
 * post-process case — the generator script changed but the spec
 * didn't, so the hash is unchanged but the TS legitimately is).
 *
 * Intent: prevent hand-edits to generated clients. The only legitimate
 * change is via `npm run generate:api`, which either updates the hash
 * (when the spec changed) or only modifies the post-process pass in
 * the generator script (when the spec is identical but the script's
 * output differs — e.g. T-0049c's `FileParameter.fileName?` normaliser).
 *
 * Wire into git via a husky/`.husky/pre-commit` script that runs this
 * (T-0016 handles the husky setup; the script itself is here so it's
 * version-controlled and testable).
 */
import { execSync } from 'node:child_process';

let staged;
try {
  staged = execSync('git diff --cached --name-only --diff-filter=AM', { encoding: 'utf8' })
    .split('\n')
    .filter(Boolean);
} catch (err) {
  console.error('[pre-commit] git diff failed:', err.message);
  process.exit(0); // not in a git repo or first commit — let it through
}

const apiClientRegex = /^frontend\/src\/lib\/api-client\/.*-api\.v1\.ts$/;
const hashesFile = 'frontend/src/lib/api-client/.spec-hashes.json';
const generatorScript = 'frontend/scripts/generate-api.mjs';

const touchedGenerated = staged.filter((p) => apiClientRegex.test(p));
const touchedHashes = staged.includes(hashesFile);
const touchedGenerator = staged.includes(generatorScript);

if (touchedGenerated.length > 0 && !touchedHashes && !touchedGenerator) {
  console.error(
    '[pre-commit] You modified generated API client file(s) without staging\n' +
    '             frontend/src/lib/api-client/.spec-hashes.json OR\n' +
    '             frontend/scripts/generate-api.mjs:\n' +
    touchedGenerated.map((p) => '             - ' + p).join('\n') + '\n\n' +
    '             Generated clients must be regenerated via `npm run generate:api`,\n' +
    '             which either updates .spec-hashes.json (when the spec changed)\n' +
    '             or only changes the generator\'s post-process pass (when the spec\n' +
    '             is identical but the script\'s output differs). Manual edits are\n' +
    '             rejected per ADR 0022.\n'
  );
  process.exit(1);
}
