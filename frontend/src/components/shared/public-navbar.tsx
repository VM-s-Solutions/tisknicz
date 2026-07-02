"use client";

import { useState } from 'react';
import Link from 'next/link';
import { MakablesLogo } from '@/components/shared/makables-logo';
import { t } from '@/lib/i18n';

const NAV_LINKS = [
  { href: '/', key: 'nav.home' as const },
  { href: '/katalog', key: 'nav.catalog' as const },
  { href: '/jak-to-funguje', key: 'nav.how_it_works' as const },
  { href: '/pro-makery', key: 'nav.for_makers' as const },
];

export function PublicNavbar() {
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  function closeMobileMenu(): void {
    setIsMobileMenuOpen(false);
  }

  return (
    <header className="relative sticky top-0 z-50 border-b border-zinc-800 bg-surface-primary/95 backdrop-blur supports-[backdrop-filter]:bg-surface-primary/80">
      <div className="mx-auto flex w-full max-w-7xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
        <Link
          href="/"
          className="inline-flex items-center transition-opacity hover:opacity-90"
          aria-label="Makables"
        >
          <MakablesLogo textClassName="text-lg font-semibold tracking-tight text-zinc-100 leading-none" />
        </Link>

        <nav className="hidden items-center gap-2 md:flex" aria-label={t('nav.public_aria')}>
          {NAV_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="rounded-lg px-3 py-2 text-sm font-medium text-zinc-300 transition-colors hover:bg-zinc-800 hover:text-white"
            >
              {t(link.key)}
            </Link>
          ))}
        </nav>

        <div className="hidden items-center gap-2 md:flex">
          <Link
            href="/login"
            className="rounded-lg border border-zinc-700 px-4 py-2 text-sm font-semibold text-zinc-200 transition-colors hover:border-zinc-500 hover:bg-zinc-800"
          >
            {t('nav.login')}
          </Link>
          <Link
            href="/register?type=maker"
            className="rounded-lg border border-brand-500 bg-brand-500 px-4 py-2 text-sm font-semibold text-zinc-950 transition-colors hover:bg-brand-400"
          >
            {t('nav.start_selling')}
          </Link>
        </div>

        <button
          type="button"
          className="inline-flex items-center justify-center rounded-lg border border-zinc-700 px-3 py-2 text-sm font-semibold text-zinc-200 transition-colors hover:border-zinc-500 hover:bg-zinc-800 md:hidden"
          aria-expanded={isMobileMenuOpen}
          aria-controls="public-mobile-menu"
          aria-label={isMobileMenuOpen ? t('nav.close_menu') : t('nav.open_menu')}
          onClick={() => setIsMobileMenuOpen((current) => !current)}
        >
          <span className="sr-only">{isMobileMenuOpen ? t('nav.close_menu') : t('nav.open_menu')}</span>
          <span className="relative block h-4 w-5" aria-hidden="true">
            <span
              className={`absolute left-0 top-0 block h-0.5 w-5 bg-current transition-transform duration-300 ${isMobileMenuOpen ? 'translate-y-2 rotate-45' : ''}`}
            />
            <span
              className={`absolute left-0 top-2 block h-0.5 w-5 bg-current transition-opacity duration-300 ${isMobileMenuOpen ? 'opacity-0' : 'opacity-100'}`}
            />
            <span
              className={`absolute left-0 top-4 block h-0.5 w-5 bg-current transition-transform duration-300 ${isMobileMenuOpen ? '-translate-y-2 -rotate-45' : ''}`}
            />
          </span>
        </button>
      </div>

      <div
        id="public-mobile-menu"
        className={`absolute inset-x-0 top-full z-40 border-t border-zinc-800 bg-surface-primary/95 backdrop-blur transition-all duration-300 md:hidden ${isMobileMenuOpen ? 'translate-y-0 opacity-100' : '-translate-y-2 opacity-0 pointer-events-none'}`}
      >
        <div className="px-4 pb-4 sm:px-6">
          <nav className="flex flex-col gap-1 pt-3" aria-label={t('nav.public_aria')}>
            {NAV_LINKS.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                className="rounded-lg px-3 py-2 text-sm font-medium text-zinc-300 transition-colors hover:bg-zinc-800 hover:text-white"
                onClick={closeMobileMenu}
              >
                {t(link.key)}
              </Link>
            ))}
          </nav>

          <div className="mt-3 flex flex-col gap-2">
            <Link
              href="/login"
              className="rounded-lg border border-zinc-700 px-4 py-2 text-center text-sm font-semibold text-zinc-200 transition-colors hover:border-zinc-500 hover:bg-zinc-800"
              onClick={closeMobileMenu}
            >
              {t('nav.login')}
            </Link>
            <Link
              href="/register?type=maker"
              className="rounded-lg border border-brand-500 bg-brand-500 px-4 py-2 text-center text-sm font-semibold text-zinc-950 transition-colors hover:bg-brand-400"
              onClick={closeMobileMenu}
            >
              {t('nav.start_selling')}
            </Link>
          </div>
        </div>
      </div>
    </header>
  );
}
