/**
 * Multi-worker entry point for the Next.js `output: 'standalone'` server.
 *
 * WHY (measured 2026-08-22 against web-makables-weu-dev):
 *   `node server.js` is ONE process on ONE JS thread, and it does SSR *and*
 *   proxies every browser API call through the /api-proxy rewrite (T-0153)
 *   *and* runs the next/image optimizer. Six always-on runtimes share the
 *   single 2-vCPU Linux plan (4 .NET API hosts + Functions + this), and the
 *   .NET hosts are multi-threaded — so the frontend loses the CPU fight with
 *   a single runnable thread while the backends stay fast.
 *
 *   Black-box ramp on the deployed dev app (time_total, seconds):
 *     /vop     (zero API calls)  conc=1 0.18 → conc=16 0.62–1.13
 *     /katalog (SSR + 2 fetches) conc=1 0.68 → conc=16 1.79–3.66
 *     direct API /health                       0.025 warm
 *   A page with no backend call degrading 6x proves it is Node CPU, not the
 *   network, not the database and not a handler.
 *
 * Node's cluster module shares ONE listening socket across the workers and
 * round-robins accepted connections, so `PORT` stays 8080 and App Service's
 * probe is unaffected. Nothing in the app is stateful per-process: Next's
 * in-memory response cache is a cache, and sessions live in HttpOnly cookies
 * validated by the .NET hosts (ADR 0012), so a request may land on any worker.
 *
 * This does NOT replace giving the frontend its own App Service Plan — it
 * just stops the frontend from being structurally limited to one thread of
 * the two the plan has.
 */
'use strict';

const cluster = require('node:cluster');
const os = require('node:os');
const path = require('node:path');

const SERVER_ENTRY = path.join(__dirname, 'server.js');

/**
 * Worker count. `WEB_CLUSTER_WORKERS` (App Service setting) wins so the plan
 * can be resized without a redeploy; otherwise use every core the container
 * reports, capped at 4 — beyond the core count the workers only fight each
 * other, and each one costs ~150–250 MB of the plan's shared RAM.
 */
function resolveWorkerCount() {
  const configured = Number.parseInt(process.env.WEB_CLUSTER_WORKERS ?? '', 10);
  if (Number.isInteger(configured) && configured > 0) {
    return Math.min(configured, 8);
  }
  const cores = typeof os.availableParallelism === 'function' ? os.availableParallelism() : os.cpus().length;
  return Math.max(1, Math.min(cores, 4));
}

// Exported for the unit test; the bootstrap below is guarded so importing
// this file never starts a server.
module.exports = { resolveWorkerCount };

if (require.main === module) {
  const workerCount = resolveWorkerCount();
  // One worker buys nothing but an extra idle process and a second RSS
  // baseline — run the server in-process instead. Above one, `exec` in
  // runPrimary points the forks straight at server.js, so a worker never
  // loads this file.
  if (workerCount === 1) {
    require(SERVER_ENTRY);
  } else {
    runPrimary(workerCount);
  }
}

function runPrimary(count) {
  cluster.setupPrimary({ exec: SERVER_ENTRY });

  console.log(`[cluster] starting ${count} Next.js workers on port ${process.env.PORT ?? 3000}`);

  for (let i = 0; i < count; i += 1) {
    cluster.fork();
  }

  // Crash-loop guard: a worker that dies because the BUILD is broken (missing
  // env var, unresolved import) dies instantly, every time. Reforking forever
  // would hide that behind a "Running" site that 502s — exit instead and let
  // App Service surface the container failure.
  const RESTART_WINDOW_MS = 60_000;
  const MAX_RESTARTS_PER_WINDOW = count * 5;
  let restarts = 0;
  let windowStartedAt = process.hrtime.bigint();
  let shuttingDown = false;

  cluster.on('exit', (worker, code, signal) => {
    if (shuttingDown) return;

    const elapsedMs = Number(process.hrtime.bigint() - windowStartedAt) / 1e6;
    if (elapsedMs > RESTART_WINDOW_MS) {
      restarts = 0;
      windowStartedAt = process.hrtime.bigint();
    }
    restarts += 1;

    if (restarts > MAX_RESTARTS_PER_WINDOW) {
      console.error(
        `[cluster] ${restarts} worker exits within ${Math.round(elapsedMs)}ms — refusing to respawn, exiting so the container restarts`,
      );
      process.exit(1);
    }

    console.error(`[cluster] worker ${worker.process.pid} exited (code=${code}, signal=${signal}) — respawning`);
    cluster.fork();
  });

  // App Service sends SIGTERM on stop/restart/deploy. Forward it so each
  // worker finishes its in-flight responses instead of dropping connections.
  for (const signal of ['SIGTERM', 'SIGINT']) {
    process.on(signal, () => {
      shuttingDown = true;
      console.log(`[cluster] ${signal} — stopping ${Object.keys(cluster.workers ?? {}).length} workers`);
      for (const worker of Object.values(cluster.workers ?? {})) {
        worker.kill(signal);
      }
    });
  }
}
