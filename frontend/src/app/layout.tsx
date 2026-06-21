import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { SITE_URL } from '@/lib/seo/site-url';
import './globals.css';

const inter = Inter({
  subsets: ['latin', 'latin-ext'],
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
  return (
    <html lang="cs" className={`${inter.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-surface-primary font-sans">
        <main className="flex-1">{children}</main>
      </body>
    </html>
  );
}
