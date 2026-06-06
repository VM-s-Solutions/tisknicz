#!/usr/bin/env node
// scripts/check-consistency.mjs — Makables monorepo consistency linter.
//
// Static checks for the seven canonical patterns in docs/architecture/patterns.md
// that the language servers / dotnet build do NOT catch (one-file feature shape,
// inline error strings, money column naming, etc.). Designed to run pre-commit
// and in CI; a baseline file lets us land the script without rewriting the
// entire backlog of grandfathered tickets in one PR.
//
// Usage:
//   node scripts/check-consistency.mjs
//   node scripts/check-consistency.mjs --paths=backend/src/**/*.cs
//   node scripts/check-consistency.mjs --baseline=docs/audits/consistency-violations.md
//   node scripts/check-consistency.mjs --update-baseline
//   node scripts/check-consistency.mjs --json
//
// Exit codes: 0 = clean (or baseline-only), 1 = new findings, 2 = script error.
// Node 20+, ESM, zero npm dependencies.

import { readdir, readFile, writeFile, stat, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join, relative, resolve, dirname, sep, posix } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const DEFAULT_BASELINE = 'docs/audits/consistency-violations.md';

// ---------------------------------------------------------------------------
// arg parsing
// ---------------------------------------------------------------------------

function parseArgs(argv) {
    const out = { paths: null, baseline: DEFAULT_BASELINE, updateBaseline: false, json: false };
    for (const a of argv.slice(2)) {
        if (a.startsWith('--paths=')) out.paths = a.slice('--paths='.length);
        else if (a.startsWith('--baseline=')) out.baseline = a.slice('--baseline='.length);
        else if (a === '--update-baseline') out.updateBaseline = true;
        else if (a === '--json') out.json = true;
        else if (a === '-h' || a === '--help') { printHelp(); process.exit(0); }
        else { process.stderr.write(`check-consistency: unknown arg "${a}"\n`); process.exit(2); }
    }
    return out;
}

function printHelp() {
    process.stdout.write(
        'Usage: node scripts/check-consistency.mjs [--paths=<glob>] ' +
        '[--baseline=<file>] [--update-baseline] [--json]\n',
    );
}

// ---------------------------------------------------------------------------
// fs walking + POSIX-style glob matching
// ---------------------------------------------------------------------------

const IGNORED_DIRS = new Set(['node_modules', 'bin', 'obj', '.git', '.next', 'dist', 'out']);

// Glob patterns (POSIX-style, repo-relative) for paths that are auto-generated
// and not edited by hand — flagging violations here is noise. CLAUDE.md forbids
// manual edits to `lib/api-client/`; EF Core migration Designer + snapshot files
// are emitted by `dotnet ef migrations add` and re-emitted on every regen.
const IGNORED_PATH_GLOBS = [
    'frontend/src/lib/api-client/**',
    'backend/src/Makables.Infra.Database/Migrations/*.Designer.cs',
    'backend/src/Makables.Infra.Database/Migrations/MakablesDbContextModelSnapshot.cs',
];

async function walk(root) {
    const acc = [];
    async function recur(dir) {
        let entries;
        try { entries = await readdir(dir, { withFileTypes: true }); }
        catch { return; }
        for (const e of entries) {
            if (IGNORED_DIRS.has(e.name)) continue;
            const full = join(dir, e.name);
            if (e.isDirectory()) await recur(full);
            else if (e.isFile()) acc.push(full);
        }
    }
    await recur(root);
    return acc;
}

function toPosix(p) { return p.split(sep).join('/'); }

// minimal glob: supports **, *, ?, and brace alternation {a,b,c}
function globToRegex(glob) {
    let re = '^';
    let i = 0;
    while (i < glob.length) {
        const c = glob[i];
        if (c === '*' && glob[i + 1] === '*') {
            re += '.*'; i += 2;
            if (glob[i] === '/') i++;
        } else if (c === '*') { re += '[^/]*'; i++; }
        else if (c === '?') { re += '[^/]'; i++; }
        else if (c === '{') {
            const close = glob.indexOf('}', i);
            if (close === -1) { re += '\\{'; i++; continue; }
            const opts = glob.slice(i + 1, close).split(',').map(o => o.replace(/[.+^$()|\\\[\]]/g, '\\$&'));
            re += '(' + opts.join('|') + ')';
            i = close + 1;
        } else if ('.+^$()|[]\\'.includes(c)) { re += '\\' + c; i++; }
        else { re += c; i++; }
    }
    return new RegExp(re + '$');
}

function matchGlob(relPosix, glob) { return globToRegex(glob).test(relPosix); }

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

function lineOf(src, idx) {
    let line = 1;
    for (let i = 0; i < idx && i < src.length; i++) if (src.charCodeAt(i) === 10) line++;
    return line;
}

function stripComments(src) {
    // remove // line comments and /* */ block comments (good enough for our checks)
    return src.replace(/\/\*[\s\S]*?\*\//g, m => m.replace(/[^\n]/g, ' '))
              .replace(/\/\/[^\n]*/g, m => m.replace(/[^\n]/g, ' '));
}

function finding(file, line, ruleId, message) {
    return { file: toPosix(relative(REPO_ROOT, file)), line, ruleId, message };
}

// ---------------------------------------------------------------------------
// rules
// ---------------------------------------------------------------------------

// T1: one-file feature shape (see patterns.md §A.3)
function ruleT1(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    if (!matchGlob(rel, 'backend/src/Makables.Core.AppServices/Features/**/*.cs')) return [];
    const code = stripComments(src);
    const out = [];

    // count top-level class/record declarations (cheap heuristic — namespace-scoped types)
    // we look for non-nested `public static class` / `public sealed class` whose match
    // index is not preceded by a `class` keyword on a containing line. Approximation:
    // top-level types are those whose declaration column is 0 in the namespace-scoped
    // file (file-scoped namespace pattern Makables uses everywhere).
    const topLevelTypes = [...code.matchAll(/^\s*public\s+(?:static\s+|sealed\s+|abstract\s+)?(?:partial\s+)?(class|record|interface|struct|enum)\s+(\w+)/gm)]
        .filter(m => m[0].startsWith('public') || m[0].match(/^\s{0,3}public/));
    if (topLevelTypes.length === 0) {
        out.push(finding(file, 1, 'T1', 'feature file must declare a public static class wrapper'));
        return out;
    }
    if (topLevelTypes.length > 1) {
        for (let i = 1; i < topLevelTypes.length; i++) {
            const m = topLevelTypes[i];
            out.push(finding(file, lineOf(code, m.index), 'T1',
                `multiple top-level types in feature file ("${m[2]}") — one use case per file`));
        }
    }

    // require nested Command|Query + Response + Validator + Handler
    const hasCommand = /\brecord\s+Command\b/.test(code);
    const hasQuery = /\brecord\s+Query\b/.test(code);
    const hasResponse = /\brecord\s+Response\b/.test(code);
    const hasValidator = /\bclass\s+Validator\b/.test(code);
    const hasHandler = /\bclass\s+Handler\b/.test(code);

    if (!hasCommand && !hasQuery)
        out.push(finding(file, 1, 'T1', 'feature file is missing nested "record Command" or "record Query"'));
    if (!hasResponse)
        out.push(finding(file, 1, 'T1', 'feature file is missing nested "record Response"'));
    if (!hasValidator)
        out.push(finding(file, 1, 'T1', 'feature file is missing nested "class Validator"'));
    if (!hasHandler)
        out.push(finding(file, 1, 'T1', 'feature file is missing nested "class Handler"'));

    return out;
}

// T2: no console.* in frontend (patterns.md §B.7)
const T2_ALLOW = new Set([
    'frontend/src/lib/runtime/api-fetch.ts',
    'frontend/src/lib/logger.ts',
]);
function ruleT2(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    if (!matchGlob(rel, 'frontend/src/**/*.{ts,tsx}')) return [];
    if (T2_ALLOW.has(rel)) return [];
    const code = stripComments(src);
    const out = [];
    for (const m of code.matchAll(/\bconsole\.(log|info|warn|error|debug|trace)\s*\(/g)) {
        out.push(finding(file, lineOf(code, m.index), 'T2',
            `console.${m[1]} forbidden in frontend — inject the structured logger instead`));
    }
    return out;
}

// T3: no SaveChangesAsync in AppServices (patterns.md §A.7 UoW pipeline)
function ruleT3(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    if (!matchGlob(rel, 'backend/src/Makables.Core.AppServices/**/*.cs')) return [];
    const code = stripComments(src);
    const out = [];
    for (const m of code.matchAll(/\bSaveChangesAsync\s*\(/g)) {
        out.push(finding(file, lineOf(code, m.index), 'T3',
            'SaveChangesAsync is forbidden in handlers — UnitOfWorkPipelineBehavior commits'));
    }
    return out;
}

// T4: type safety — no `dynamic` in C#, no `: any` / `as any` in TS
function ruleT4(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    const out = [];
    if (matchGlob(rel, 'backend/src/**/*.cs')) {
        const code = stripComments(src);
        for (const m of code.matchAll(/\bdynamic\b/g)) {
            out.push(finding(file, lineOf(code, m.index), 'T4',
                '`dynamic` is forbidden — model the contract with a concrete type'));
        }
    }
    if (matchGlob(rel, 'frontend/src/**/*.{ts,tsx}')) {
        const code = stripComments(src);
        for (const m of code.matchAll(/:\s*any\b/g)) {
            out.push(finding(file, lineOf(code, m.index), 'T4',
                '`: any` is forbidden — type the value (use `unknown` if truly opaque)'));
        }
        for (const m of code.matchAll(/\bas\s+any\b/g)) {
            out.push(finding(file, lineOf(code, m.index), 'T4',
                '`as any` is forbidden — narrow with a type guard instead'));
        }
    }
    return out;
}

// T5: Error.X calls must reference BusinessErrorMessage.X, not an inline string
function ruleT5(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    if (!matchGlob(rel, 'backend/src/**/*.cs')) return [];
    const code = stripComments(src);
    const out = [];
    const methods = ['Conflict', 'NotFound', 'Permanent', 'Validation', 'Unauthorized', 'Forbidden'];
    // match Error.<Method>( ... ) and capture the argument list up to the matching paren
    const re = new RegExp(`\\bError\\.(${methods.join('|')})\\s*\\(`, 'g');
    for (const m of code.matchAll(re)) {
        const argStart = m.index + m[0].length;
        let depth = 1, i = argStart;
        while (i < code.length && depth > 0) {
            const c = code[i];
            if (c === '(') depth++;
            else if (c === ')') depth--;
            else if (c === '"') { // skip string literal
                i++;
                while (i < code.length && code[i] !== '"') {
                    if (code[i] === '\\') i++;
                    i++;
                }
            }
            i++;
        }
        const args = code.slice(argStart, i - 1);
        // a compliant call must reference BusinessErrorMessage somewhere in its args
        // (a string literal arg without that reference = inline message)
        const hasInlineString = /"[^"]*"/.test(args);
        const hasBusinessRef = /\bBusinessErrorMessage\b/.test(args);
        if (hasInlineString && !hasBusinessRef) {
            out.push(finding(file, lineOf(code, m.index), 'T5',
                `Error.${m[1]} call uses an inline string — reference BusinessErrorMessage.X`));
        }
    }
    return out;
}

// T6: money columns — bigint columns whose name matches /amount|price|total|fee|payout/i
// must end with _minor (patterns.md §A.11 + ADR-0009 money rounding)
function ruleT6(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    const isMig = matchGlob(rel, 'backend/src/Makables.Infra.Database/Migrations/*.cs');
    const isCfg = matchGlob(rel, 'backend/src/Makables.Infra.Database/Configurations/*.cs');
    if (!isMig && !isCfg) return [];
    const code = stripComments(src);
    const out = [];
    const moneyName = /(amount|price|total|fee|payout)/i;

    if (isMig) {
        // migrationBuilder.AddColumn<long>(name: "foo_price_minor", ... type: "bigint", ...)
        // and table.Column<long>(name: "foo", type: "bigint", ...) inside CreateTable.
        // we accept the column as a slab and inspect name+type together.
        const slabRe = /(?:AddColumn|Column)\s*<\s*long\s*>\s*\(([\s\S]*?)\)\s*[,;)]/g;
        for (const m of code.matchAll(slabRe)) {
            const slab = m[1];
            const nameMatch = slab.match(/name\s*:\s*"([^"]+)"/);
            const typeMatch = slab.match(/type\s*:\s*"([^"]+)"/);
            if (!nameMatch || !typeMatch) continue;
            const name = nameMatch[1];
            const type = typeMatch[1].toLowerCase();
            if (type !== 'bigint') continue;
            if (!moneyName.test(name)) continue;
            if (!name.endsWith('_minor')) {
                out.push(finding(file, lineOf(code, m.index), 'T6',
                    `money column "${name}" must end with _minor (bigint + amount/price/total/fee/payout)`));
            }
        }
    }

    if (isCfg) {
        // HasColumnName("foo_price_minor")... HasColumnType("bigint") on the same Property chain.
        // We scan Property(...) statements as ;-terminated slabs.
        const stmts = code.split(';');
        let cursor = 0;
        for (const stmt of stmts) {
            const start = cursor;
            cursor += stmt.length + 1;
            if (!/\bHasColumnName\s*\(/.test(stmt)) continue;
            const nameM = stmt.match(/HasColumnName\s*\(\s*"([^"]+)"\s*\)/);
            if (!nameM) continue;
            const name = nameM[1];
            if (!moneyName.test(name)) continue;
            const typeM = stmt.match(/HasColumnType\s*\(\s*"([^"]+)"\s*\)/);
            const type = typeM ? typeM[1].toLowerCase() : null;
            // If type is not declared we can't be sure it's bigint — skip (EF infers from CLR
            // type long). To stay conservative, we still flag money-named columns missing _minor
            // when the declared CLR property name on the same chain ends with "Minor" — that
            // signals an intentional money property whose column drifted.
            const propM = stmt.match(/Property\s*\(\s*\w+\s*=>\s*\w+\.(\w+)\s*\)/);
            const clrName = propM ? propM[1] : null;
            const isBigint = type === 'bigint' || (clrName && clrName.endsWith('Minor'));
            if (!isBigint) continue;
            if (!name.endsWith('_minor')) {
                out.push(finding(file, lineOf(code, start), 'T6',
                    `money column "${name}" must end with _minor (CLR property "${clrName ?? '?'}")`));
            }
        }
    }

    return out;
}

// T7: no useEffect data fetching (patterns.md §B.4)
function ruleT7(file, src) {
    const rel = toPosix(relative(REPO_ROOT, file));
    if (!matchGlob(rel, 'frontend/src/**/*.{ts,tsx}')) return [];
    const code = stripComments(src);
    const out = [];
    const re = /\buseEffect\s*\(\s*(?:async\s*)?\(\s*\)\s*=>\s*\{/g;
    for (const m of code.matchAll(re)) {
        const bodyStart = m.index + m[0].length;
        let depth = 1, i = bodyStart;
        while (i < code.length && depth > 0) {
            const c = code[i];
            if (c === '{') depth++;
            else if (c === '}') depth--;
            i++;
        }
        const body = code.slice(bodyStart, i - 1);
        if (/\bfetch\s*\(|\bapiClient\.|\bawait\s+client\./.test(body)) {
            out.push(finding(file, lineOf(code, m.index), 'T7',
                'useEffect must not fetch data — fetch in a Server Component or an event handler'));
        }
    }
    return out;
}

const RULES = [ruleT1, ruleT2, ruleT3, ruleT4, ruleT5, ruleT6, ruleT7];

// ---------------------------------------------------------------------------
// baseline I/O
// ---------------------------------------------------------------------------

function findingKey(f) { return `${f.file}:${f.line}:${f.ruleId}`; }

async function readBaseline(baselinePath) {
    const abs = resolve(REPO_ROOT, baselinePath);
    if (!existsSync(abs)) return new Set();
    const src = await readFile(abs, 'utf8');
    const keys = new Set();
    for (const raw of src.split(/\r?\n/)) {
        const line = raw.trim();
        if (!line || line.startsWith('#') || line.startsWith('<!--')) continue;
        // accept "- path:line:rule" or "path:line:rule"
        const m = line.match(/^-?\s*([^\s:]+):(\d+):(T\d+)\b/);
        if (m) keys.add(`${m[1]}:${m[2]}:${m[3]}`);
    }
    return keys;
}

async function writeBaseline(baselinePath, findings) {
    const abs = resolve(REPO_ROOT, baselinePath);
    await mkdir(dirname(abs), { recursive: true });
    const sorted = [...findings].sort((a, b) =>
        a.file.localeCompare(b.file) || a.line - b.line || a.ruleId.localeCompare(b.ruleId));
    const lines = [
        '# Consistency baseline',
        '',
        '<!-- Auto-generated by scripts/check-consistency.mjs --update-baseline.',
        '     Each row is `path:line:ruleId` — a known, grandfathered violation.',
        '     New findings outside this list break the build. Shrink, never grow. -->',
        '',
    ];
    for (const f of sorted) {
        lines.push(`- ${findingKey(f)}  ${f.message}`);
    }
    lines.push('');
    await writeFile(abs, lines.join('\n'), 'utf8');
}

// ---------------------------------------------------------------------------
// output rendering
// ---------------------------------------------------------------------------

function renderTable(findings, label, stream) {
    if (findings.length === 0) return;
    stream.write(`\n${label} (${findings.length}):\n`);
    const w = {
        file: Math.min(60, Math.max(4, ...findings.map(f => f.file.length))),
        line: Math.max(4, ...findings.map(f => String(f.line).length)),
        rule: 4,
    };
    stream.write(`  ${'FILE'.padEnd(w.file)}  ${'LINE'.padStart(w.line)}  ${'RULE'.padEnd(w.rule)}  MESSAGE\n`);
    for (const f of findings) {
        stream.write(`  ${f.file.padEnd(w.file)}  ${String(f.line).padStart(w.line)}  ${f.ruleId.padEnd(w.rule)}  ${f.message}\n`);
    }
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------

async function main() {
    const args = parseArgs(process.argv);

    const allFiles = await walk(REPO_ROOT);
    const candidates = allFiles.filter(f => {
        const rel = toPosix(relative(REPO_ROOT, f));
        if (args.paths && !matchGlob(rel, args.paths)) return false;
        if (IGNORED_PATH_GLOBS.some(g => matchGlob(rel, g))) return false;
        return /\.(cs|ts|tsx|mjs|cjs)$/.test(rel);
    });

    const findings = [];
    for (const file of candidates) {
        let src;
        try { src = await readFile(file, 'utf8'); }
        catch { continue; }
        for (const rule of RULES) {
            try { findings.push(...rule(file, src)); }
            catch (err) {
                process.stderr.write(`check-consistency: rule ${rule.name} crashed on ${file}: ${err.message}\n`);
                process.exit(2);
            }
        }
    }

    if (args.updateBaseline) {
        await writeBaseline(args.baseline, findings);
        process.stderr.write(`check-consistency: wrote ${findings.length} findings to ${args.baseline}\n`);
        process.exit(0);
    }

    const baseline = await readBaseline(args.baseline);
    const tracked = findings.filter(f => baseline.has(findingKey(f)));
    const fresh = findings.filter(f => !baseline.has(findingKey(f)));

    if (args.json) {
        process.stdout.write(JSON.stringify({ new: fresh, tracked }, null, 2) + '\n');
    } else {
        renderTable(tracked, 'TRACKED (baseline)', process.stderr);
        renderTable(fresh, 'NEW findings', process.stdout);
        if (fresh.length === 0) {
            process.stdout.write(`\ncheck-consistency: clean (${tracked.length} tracked).\n`);
        } else {
            process.stdout.write(`\ncheck-consistency: ${fresh.length} new finding(s). ` +
                `Fix them, or run with --update-baseline if intentionally grandfathered.\n`);
        }
    }

    process.exit(fresh.length === 0 ? 0 : 1);
}

main().catch(err => {
    process.stderr.write(`check-consistency: ${err.stack ?? err.message}\n`);
    process.exit(2);
});
