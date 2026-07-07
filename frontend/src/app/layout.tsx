import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { CookieConsentBanner } from '@/components/shared/cookie-consent-banner';
import { SITE_URL } from '@/lib/seo/site-url';
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
    <html lang="cs" className={`${inter.variable} h-full antialiased`}>
      <head>
        {apiOrigin ? <link rel="preconnect" href={apiOrigin} crossOrigin="" /> : null}
      </head>
      <body className="flex min-h-full flex-col bg-surface-primary font-sans">
        <main className="flex-1">{children}</main>
        <CookieConsentBanner />
      </body>
    </html>
  );
}
