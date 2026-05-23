#!/usr/bin/env node
/**
 * Cross-platform husky install wrapper.
 *
 * Runs from `frontend/` (where it's invoked by npm's `prepare` lifecycle).
 * Walks up one directory to the repo root and, if a `.git` directory is
 * present, runs `husky .husky` to register the hooks. On a shallow
 * checkout (CI / Vercel build) the `.git` directory is absent and we
 * silently skip — `prepare` running on every `npm install` should never
 * tank a build.
 *
 * The previous bash-style one-liner (`cd .. && test -d .git && husky .husky`)
 * failed on Windows `cmd.exe` because `test` and `||` are not valid there.
 * Node is the lowest common denominator already in scope.
 */
import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..', '..');
const gitDir = resolve(repoRoot, '.git');

if (!existsSync(gitDir)) {
  console.log('[husky] Skipping setup: not a git checkout (CI / Vercel build).');
  process.exit(0);
}

// Resolve husky from the local node_modules so we don't depend on npx
// reaching out to the registry. If husky isn't installed (e.g. someone
// running `npm install --omit=dev`), skip silently.
const huskyBin = resolve(here, '..', 'node_modules', 'husky', 'bin.js');
if (!existsSync(huskyBin)) {
  console.log('[husky] Skipping setup: husky not installed (likely a production install).');
  process.exit(0);
}

const result = spawnSync(process.execPath, [huskyBin, '.husky'], {
  cwd: repoRoot,
  stdio: 'inherit',
});

process.exit(result.status ?? 0);
