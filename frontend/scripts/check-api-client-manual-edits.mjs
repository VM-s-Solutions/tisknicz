#!/usr/bin/env node
/**
 * Pre-commit hook per ADR 0022. Rejects commits that modify any
 * `src/lib/api-client/*-api.v1.ts` file without also updating
 * `src/lib/api-client/.spec-hashes.json`.
 *
 * Intent: prevent hand-edits to generated clients. The only legitimate
 * change is via `npm run generate:api`, which always also updates
 * `.spec-hashes.json`.
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

const touchedGenerated = staged.filter((p) => apiClientRegex.test(p));
const touchedHashes = staged.includes(hashesFile);

if (touchedGenerated.length > 0 && !touchedHashes) {
  console.error(
    '[pre-commit] You modified generated API client file(s) without staging\n' +
    '             frontend/src/lib/api-client/.spec-hashes.json:\n' +
    touchedGenerated.map((p) => '             - ' + p).join('\n') + '\n\n' +
    '             Generated clients must be regenerated via `npm run generate:api`,\n' +
    '             which also updates .spec-hashes.json. Manual edits are rejected\n' +
    '             per ADR 0022.\n'
  );
  process.exit(1);
}
