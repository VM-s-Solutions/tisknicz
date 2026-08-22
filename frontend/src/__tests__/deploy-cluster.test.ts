import { createRequire } from 'node:module';
import os from 'node:os';
import { join } from 'node:path';
import { existsSync, readFileSync } from 'node:fs';

const require_ = createRequire(import.meta.url);
const clusterPath = join(process.cwd(), 'deploy', 'cluster.js');
const { resolveWorkerCount } = require_(clusterPath) as {
  resolveWorkerCount: () => number;
};

/**
 * `deploy/cluster.js` is what App Service actually executes
 * (`appCommandLine: 'node cluster.js'` in infra/bicep/modules/web-app.bicep).
 * It forks N copies of the standalone `server.js` behind one shared
 * listening socket, because a single-threaded Next process on the shared
 * 2-vCPU plan is the platform's measured bottleneck: /vop — a page with
 * zero backend calls — went 0.18 s at concurrency 1 to 0.62–1.13 s at 16.
 *
 * Only the worker-count resolution is pure logic, so that is what is
 * tested here; the fork/respawn/SIGTERM behaviour was verified by running
 * the assembled package (see the PR body).
 */
describe('deploy/cluster.js worker count', () => {
  const original = process.env.WEB_CLUSTER_WORKERS;

  afterEach(() => {
    if (original === undefined) delete process.env.WEB_CLUSTER_WORKERS;
    else process.env.WEB_CLUSTER_WORKERS = original;
  });

  it('honours WEB_CLUSTER_WORKERS so the plan can be resized without a redeploy', () => {
    process.env.WEB_CLUSTER_WORKERS = '2';
    expect(resolveWorkerCount()).toBe(2);
  });

  it('caps the configured value at 8 — more workers than cores only adds RSS and context switching', () => {
    process.env.WEB_CLUSTER_WORKERS = '64';
    expect(resolveWorkerCount()).toBe(8);
  });

  it('falls back to the core count (max 4) when the setting is absent or junk', () => {
    const cores = typeof os.availableParallelism === 'function' ? os.availableParallelism() : os.cpus().length;
    const expected = Math.max(1, Math.min(cores, 4));

    for (const value of [undefined, '', 'two', '0', '-3'] as const) {
      if (value === undefined) delete process.env.WEB_CLUSTER_WORKERS;
      else process.env.WEB_CLUSTER_WORKERS = value;
      expect({ value, workers: resolveWorkerCount() }).toEqual({ value, workers: expected });
    }
  });

  // The standalone output is traced from the app's imports, so cluster.js is
  // NOT in it — both deploy workflows must copy it next to server.js or the
  // site starts nothing and never binds its port.
  it('is copied into the deploy package by every workflow that deploys the frontend', () => {
    expect(existsSync(clusterPath)).toBe(true);

    for (const workflow of ['deploy-staging.yml', 'deploy-production.yml'] as const) {
      const yaml = readFileSync(join(process.cwd(), '..', '.github', 'workflows', workflow), 'utf8');
      expect({ workflow, copies: yaml.includes('cp deploy/cluster.js .next/standalone/cluster.js') }).toEqual({
        workflow,
        copies: true,
      });
    }
  });
});
