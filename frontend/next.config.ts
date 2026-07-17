import type { NextConfig } from "next";

/**
 * Whitelist of remote hosts <c>next/image</c> may optimize. The product
 * image controller on the .NET Public host streams blob content (ADR
 * 0011 — "All access through the backend"), so the catalog renders
 * <c>&lt;Image src=&quot;{publicHost}/api/v1/files/products/...&quot; /&gt;</c>.
 * Without this entry the optimizer would refuse the URL.
 *
 * The Public host base URL comes from <c>NEXT_PUBLIC_API_PUBLIC_BASE_URL</c>
 * (see <c>lib/runtime/api-fetch.ts</c>). We parse it at build time so
 * production / staging just need that env var set; localhost is the
 * dev-time default.
 */
function publicHostRemotePattern() {
  const raw = process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL ?? 'http://localhost:5104';
  // Same-origin proxy mode (T-0153): a relative base ('/api-proxy/public')
  // yields relative image srcs, which next/image serves without any
  // remotePatterns entry — return null and omit the pattern.
  if (raw.startsWith('/')) {
    return null;
  }
  try {
    const url = new URL(raw);
    const port = url.port || undefined;
    return {
      protocol: (url.protocol.replace(':', '') as 'http' | 'https'),
      hostname: url.hostname,
      ...(port ? { port } : {}),
      pathname: '/api/v1/files/products/**',
    };
  } catch {
    return {
      protocol: 'http' as const,
      hostname: 'localhost',
      port: '5104',
      pathname: '/api/v1/files/products/**',
    };
  }
}

/**
 * Same-origin API proxy (T-0153). On deployed environments the frontend
 * and the four API hosts live on sibling `*.azurewebsites.net` hostnames —
 * a public-suffix domain, so the ADR 0012 session cookies
 * (HttpOnly + Secure + SameSite=Strict, no Domain) can never cross
 * between them. Instead the browser talks ONLY to the frontend origin:
 * `NEXT_PUBLIC_API_<HOST>_BASE_URL=/api-proxy/<host>` and the rewrite
 * below forwards to the real API host server-side. Set-Cookie flows back
 * through the proxy and lands first-party on the frontend origin, which
 * both the browser and the SSR cookie-forwarding in
 * `lib/runtime/api-fetch.ts` can then use.
 *
 * A rewrite is only emitted for hosts whose `API_<HOST>_INTERNAL_BASE_URL`
 * is set at build time — local dev (absolute localhost bases, shared
 * `localhost` cookie domain) needs no proxy and gets none.
 *
 * Known dev-tier limitation: proxied requests reach the API from the
 * frontend App Service egress IP, so the backend's per-IP rate limits
 * (T-0136) see one shared IP. Acceptable on dev; production should front
 * everything with a shared parent domain (T-0153 follow-up).
 */
function apiProxyRewrites() {
  const targets: Record<string, string | undefined> = {
    customer: process.env.API_CUSTOMER_INTERNAL_BASE_URL,
    maker: process.env.API_MAKER_INTERNAL_BASE_URL,
    admin: process.env.API_ADMIN_INTERNAL_BASE_URL,
    public: process.env.API_PUBLIC_INTERNAL_BASE_URL,
  };
  return Object.entries(targets)
    .filter((entry): entry is [string, string] => Boolean(entry[1]))
    .map(([host, origin]) => ({
      source: `/api-proxy/${host}/:path*`,
      destination: `${origin.replace(/\/+$/, '')}/:path*`,
    }));
}

const nextConfig: NextConfig = {
  // Self-host on Azure App Service (Linux/Node) — `standalone` emits a
  // minimal `.next/standalone` server (server.js + traced node_modules) that
  // runs with `node server.js`, no full install needed at runtime. The CI
  // deploy job assembles standalone + .next/static + public into the package.
  output: 'standalone',
  // Pin the workspace root to THIS directory. Without it, Next walks up and
  // infers the MONOREPO root as the workspace (parent lockfile / .git), which
  // nests the standalone output under `.next/standalone/frontend/` — the CI
  // assemble step then fails (`.next/standalone/.next` does not exist) and
  // `node server.js` would not sit at the deploy-package root that
  // infra/bicep/modules/web-app.bicep expects. The frontend's dependency
  // closure is complete within this folder (own package-lock + node_modules).
  outputFileTracingRoot: __dirname,
  turbopack: {
    root: __dirname,
  },
  compress: true,
  poweredByHeader: false,
  async rewrites() {
    return apiProxyRewrites();
  },
  images: {
    formats: ['image/avif', 'image/webp'],
    minimumCacheTTL: 60 * 60 * 24 * 30,
    remotePatterns: [publicHostRemotePattern()].filter((pattern) => pattern !== null),
  },
};

export default nextConfig;
