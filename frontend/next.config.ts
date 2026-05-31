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

const nextConfig: NextConfig = {
  images: {
    remotePatterns: [publicHostRemotePattern()],
  },
};

export default nextConfig;
