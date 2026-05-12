import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { Header } from '@/components/layout/header';
import { Footer } from '@/components/layout/footer';
import './globals.css';

const inter = Inter({
  subsets: ['latin', 'latin-ext'],
  variable: '--font-inter',
});

export const metadata: Metadata = {
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
        <Header />
        <main className="flex-1">{children}</main>
        <Footer />
      </body>
    </html>
  );
}
