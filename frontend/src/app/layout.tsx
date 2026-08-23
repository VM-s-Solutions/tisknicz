import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { CookieConsentBanner } from '@/components/shared/cookie-consent-banner';
import { SITE_URL } from '@/lib/seo/site-url';
import { THEME_BOOTSTRAP_SCRIPT } from '@/lib/theme/theme';
import './globals.css';

function publicApiOrigin(): string | null {
  const raw = process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL;
  if (!raw) {
    return null;
  }

  try {
    return new URL(raw).origin;
  } catch {
    return null;
  }
}

const inter = Inter({
  subsets: ['latin', 'latin-ext'],
  display: 'swap',
  fallback: ['system-ui', 'arial'],
  variable: '--font-inter',
});

export const metadata: Metadata = {
  // Resolves relative openGraph.url / canonical values against the
  // canonical site host (T-0131). Without this Next warns at build and
  // resolves them against localhost in production.
  metadataBase: new URL(SITE_URL),
  title: {
    default: 'Makables — Where Ideas Take Shape',
    template: '%s | Makables',
  },
  description: 'Marketplace pro makery a tiskaře v ČR. Najdi tvůrce, objednej, nech si doručit.',
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const apiOrigin = publicApiOrigin();

  return (
    // `suppressHydrationWarning` covers exactly one attribute: `data-theme`,
    // written onto <html> by the bootstrap script below before React ever
    // sees the document. Without it every page logs a hydration mismatch on
    // the root element. It does NOT suppress warnings for the tree inside.
    <html lang="cs" className={`${inter.variable} h-full antialiased`} suppressHydrationWarning>
      <head>
        {/* Resolve the theme before first paint. A theme applied after
            hydration paints one frame of the wrong palette first, which on
            a dark default is a full-screen white flash on every load. The
            script is ~200 bytes, inline and synchronous on purpose. */}
        <script dangerouslySetInnerHTML={{ __html: THEME_BOOTSTRAP_SCRIPT }} />
        {apiOrigin ? (
          <>
            {/* Warm the API origin before the first client-side fetch:
                dns-prefetch is the low-cost fallback for browsers that
                ignore preconnect, preconnect opens TCP+TLS eagerly. */}
            <link rel="dns-prefetch" href={apiOrigin} />
            <link rel="preconnect" href={apiOrigin} crossOrigin="" />
          </>
        ) : null}
      </head>
      <body className="flex min-h-full flex-col bg-surface-primary font-sans">
        <main className="flex-1">{children}</main>
        <CookieConsentBanner />
      </body>
    </html>
  );
}
