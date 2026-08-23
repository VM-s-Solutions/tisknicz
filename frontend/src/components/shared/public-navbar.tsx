"use client";

import { useState } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { MakablesLogo } from '@/components/shared/makables-logo';
import { ThemeToggle } from '@/components/shared/theme-toggle';
import { Icon, type IconName } from '@/components/ui/icon';
import type { DisplaySession } from '@/lib/auth/display-session';
import { logout } from '@/lib/api-client-helpers/auth';
import { t, type MessageKey } from '@/lib/i18n';

const NAV_LINKS = [
  { href: '/', key: 'nav.home' as const },
  { href: '/katalog', key: 'nav.catalog' as const },
  { href: '/jak-to-funguje', key: 'nav.how_it_works' as const },
  { href: '/pro-makery', key: 'nav.for_makers' as const },
  { href: '/kontakt', key: 'nav.contact' as const },
];

interface AccountLink {
  readonly href: string;
  readonly key: MessageKey;
  readonly icon: IconName;
}

const CUSTOMER_ACCOUNT_LINKS: readonly AccountLink[] = [
  { href: '/dashboard/zakaznik/objednavky', key: 'nav.customer.orders', icon: 'package' },
  { href: '/dashboard/zakaznik/profile', key: 'nav.customer.profile', icon: 'user' },
];

const MAKER_ACCOUNT_LINKS: readonly AccountLink[] = [
  { href: '/dashboard/maker/objednavky', key: 'nav.maker.orders', icon: 'package' },
  { href: '/dashboard/maker/produkty', key: 'nav.maker.products', icon: 'grid' },
  { href: '/dashboard/maker/vyplaty', key: 'nav.maker.payouts', icon: 'wallet' },
  { href: '/dashboard/maker/recenze', key: 'nav.maker.reviews', icon: 'star' },
  { href: '/dashboard/maker/profil', key: 'nav.maker.profile', icon: 'user' },
];

interface PublicNavbarProps {
  /**
   * Display session decoded server-side by the mounting layout (see
   * `lib/auth/display-session.ts`). `null`/omitted renders the
   * logged-out state. Display-only — authorization stays with the
   * middleware + backend.
   */
  session?: DisplaySession | null;
}

export function PublicNavbar({ session = null }: PublicNavbarProps) {
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [isAccountMenuOpen, setIsAccountMenuOpen] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const pathname = usePathname();
  const router = useRouter();

  const accountLinks =
    session?.audience === 'maker' ? MAKER_ACCOUNT_LINKS : CUSTOMER_ACCOUNT_LINKS;

  function closeMobileMenu(): void {
    setIsMobileMenuOpen(false);
  }

  function isActive(href: string): boolean {
    if (href === '/') return pathname === '/';
    // T-0171 (audit PUB-L8): a product page is one hop deeper into the
    // catalog, but matched no nav item — orientation vanished exactly
    // where the visitor had gone furthest.
    if (href === '/katalog') return pathname.startsWith('/katalog') || pathname.startsWith('/produkt');
    return pathname.startsWith(href);
  }

  async function handleLogout(): Promise<void> {
    if (!session || loggingOut) return;
    setLoggingOut(true);
    // Logout is idempotent on the backend and clears the cookies even on
    // command failure; a network error still warrants leaving the UI in
    // a logged-in state the user can retry from.
    setLogoutError(null);
    const result = await logout(session.audience);
    setLoggingOut(false);
    if (result.success) {
      setIsAccountMenuOpen(false);
      setIsMobileMenuOpen(false);
      router.push('/');
      // Re-render the server tree so every session-aware surface picks
      // up the cleared cookie.
      router.refresh();
      return;
    }
    // T-0171 (audit PUB-M6): a failed logout used to be completely
    // silent — the label flickered and nothing else happened, leaving
    // the user unsure whether they were still signed in.
    setLogoutError(t('nav.logout_failed'));
  }

  const accountMenu = session && (
    <div className="relative">
      <button
        type="button"
        className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 py-1.5 pl-2 pr-2.5 text-sm font-medium text-zinc-200 transition-colors hover:border-zinc-600 hover:bg-zinc-800 hover:text-zinc-50"
        aria-expanded={isAccountMenuOpen}
        aria-haspopup="menu"
        onClick={() => setIsAccountMenuOpen((current) => !current)}
      >
        <span className="flex h-5 w-5 items-center justify-center rounded-md bg-tint-brand-strong text-on-tint-brand">
          <Icon name="user" size={13} strokeWidth={1.75} />
        </span>
        {t('nav.account')}
        <Icon
          name="chevronDown"
          size={14}
          className={`text-zinc-500 transition-transform duration-150 ${isAccountMenuOpen ? 'rotate-180' : ''}`}
        />
      </button>
      {isAccountMenuOpen && (
        <>
          <button
            type="button"
            className="fixed inset-0 z-40 cursor-default"
            aria-hidden="true"
            tabIndex={-1}
            onClick={() => setIsAccountMenuOpen(false)}
          />
          <div
            role="menu"
            className="absolute right-0 top-full z-50 mt-2 w-64 overflow-hidden rounded-xl border border-zinc-700 bg-surface-card elevated-shadow"
          >
            <div className="flex items-center gap-2.5 border-b border-zinc-800 bg-surface-elevated px-3 py-2.5">
              <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-tint-brand-strong text-on-tint-brand">
                <Icon name="user" size={15} strokeWidth={1.75} />
              </span>
              <p className="min-w-0 truncate text-xs text-zinc-400">{session.email}</p>
            </div>
            <div className="flex flex-col p-1.5">
              {accountLinks.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  role="menuitem"
                  className={`flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-medium transition-colors ${
                    isActive(link.href)
                      ? 'bg-zinc-800 text-zinc-50'
                      : 'text-zinc-300 hover:bg-zinc-800 hover:text-zinc-50'
                  }`}
                  onClick={() => setIsAccountMenuOpen(false)}
                >
                  <Icon
                    name={link.icon}
                    size={16}
                    className={isActive(link.href) ? 'text-brand-300' : 'text-zinc-500'}
                  />
                  {t(link.key)}
                </Link>
              ))}
            </div>
            <div className="h-px bg-zinc-800" />
            <div className="p-1.5">
              <button
                type="button"
                role="menuitem"
                className="flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left text-sm font-medium text-error transition-colors hover:bg-error-fill-soft disabled:opacity-60"
                disabled={loggingOut}
                onClick={handleLogout}
              >
                <Icon name="logOut" size={16} />
                {loggingOut ? t('nav.logging_out') : t('nav.logout')}
              </button>
              {logoutError ? (
                <p role="alert" className="px-2.5 pb-1 text-xs text-error">
                  {logoutError}
                </p>
              ) : null}
            </div>
          </div>
        </>
      )}
    </div>
  );

  return (
    <header className="sticky top-0 z-50 border-b border-zinc-800/80 bg-surface-primary/95 backdrop-blur supports-[backdrop-filter]:bg-surface-primary/80">
      <div className="mx-auto flex w-full max-w-7xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
        <Link
          href="/"
          className="inline-flex items-center hover:opacity-90"
          aria-label="Makables"
        >
          <MakablesLogo textClassName="text-lg font-semibold tracking-tight text-zinc-100 leading-none" />
        </Link>

        {/* Breakpoint is lg, not md: with five primary links the inline
            nav plus the CTA cluster no longer fits a 768px viewport, so
            tablets keep the collapsed menu. */}
        <nav className="hidden items-center gap-6 lg:flex" aria-label={t('nav.public_aria')}>
          {NAV_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className={`link-underline py-1 text-sm font-medium transition-colors ${
                isActive(link.href) ? 'text-zinc-50' : 'text-zinc-400 hover:text-zinc-50'
              }`}
              aria-current={isActive(link.href) ? 'page' : undefined}
            >
              {t(link.key)}
            </Link>
          ))}
        </nav>

        <div className="hidden items-center gap-6 lg:flex">
          <ThemeToggle />
          {session ? (
            accountMenu
          ) : (
            <>
              <Link
                href="/login"
                className="link-underline py-1 text-sm font-medium text-zinc-300 transition-colors hover:text-zinc-50"
              >
                {t('nav.login')}
              </Link>
              <Link
                href="/register?type=maker"
                className="inline-flex items-center gap-1.5 rounded-lg border border-brand-line px-4 py-1.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-brand-fill-soft"
              >
                {t('nav.start_selling')}
                <span aria-hidden="true">→</span>
              </Link>
            </>
          )}
        </div>

        <div className="flex items-center gap-1 lg:hidden">
          <ThemeToggle />
          <button
          type="button"
          className="inline-flex items-center justify-center px-2 py-2 text-zinc-300 transition-colors hover:text-zinc-50"
          aria-expanded={isMobileMenuOpen}
          aria-controls="public-mobile-menu"
          aria-label={isMobileMenuOpen ? t('nav.close_menu') : t('nav.open_menu')}
          onClick={() => setIsMobileMenuOpen((current) => !current)}
        >
          <span className="sr-only">{isMobileMenuOpen ? t('nav.close_menu') : t('nav.open_menu')}</span>
          <span className="relative block h-4 w-5" aria-hidden="true">
            <span
              className={`absolute left-0 top-0 block h-0.5 w-5 bg-current transition-transform duration-200 ${isMobileMenuOpen ? 'translate-y-2 rotate-45' : ''}`}
            />
            <span
              className={`absolute left-0 top-2 block h-0.5 w-5 bg-current transition-opacity duration-200 ${isMobileMenuOpen ? 'opacity-0' : 'opacity-100'}`}
            />
            <span
              className={`absolute left-0 top-4 block h-0.5 w-5 bg-current transition-transform duration-200 ${isMobileMenuOpen ? '-translate-y-2 -rotate-45' : ''}`}
            />
          </span>
          </button>
        </div>
      </div>

      <div
        id="public-mobile-menu"
        className={`absolute inset-x-0 top-full z-40 border-t border-zinc-800 bg-zinc-950/95 backdrop-blur lg:hidden ${isMobileMenuOpen ? 'block' : 'hidden'}`}
      >
        <div className="px-4 pb-5 sm:px-6">
          <nav className="flex flex-col pt-2" aria-label={t('nav.public_aria')}>
            {NAV_LINKS.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                className={`border-b border-zinc-800/60 px-1 py-3 text-sm font-medium transition-colors ${
                  isActive(link.href) ? 'text-zinc-50' : 'text-zinc-400 hover:text-zinc-50'
                }`}
                aria-current={isActive(link.href) ? 'page' : undefined}
                onClick={closeMobileMenu}
              >
                {t(link.key)}
              </Link>
            ))}
          </nav>

          {session ? (
            <div className="mt-4 flex flex-col px-1">
              <div className="flex items-center gap-2.5 pb-3 pt-1">
                <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-tint-brand-strong text-on-tint-brand">
                  <Icon name="user" size={15} strokeWidth={1.75} />
                </span>
                <p className="min-w-0 truncate text-xs text-zinc-400">{session.email}</p>
              </div>
              {accountLinks.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  className={`flex items-center gap-3 border-t border-zinc-800/60 px-1 py-3 text-sm font-medium transition-colors ${
                    isActive(link.href) ? 'text-zinc-50' : 'text-zinc-300 hover:text-zinc-50'
                  }`}
                  onClick={closeMobileMenu}
                >
                  <Icon
                    name={link.icon}
                    size={16}
                    className={isActive(link.href) ? 'text-brand-300' : 'text-zinc-500'}
                  />
                  {t(link.key)}
                </Link>
              ))}
              <button
                type="button"
                className="flex items-center gap-3 border-t border-zinc-800/60 px-1 py-3 text-left text-sm font-medium text-error transition-colors hover:text-error/80 disabled:opacity-60"
                disabled={loggingOut}
                onClick={handleLogout}
              >
                <Icon name="logOut" size={16} />
                {loggingOut ? t('nav.logging_out') : t('nav.logout')}
              </button>
            </div>
          ) : (
            <div className="mt-4 flex items-center gap-6 px-1">
              <Link
                href="/login"
                className="link-underline py-1 text-sm font-medium text-zinc-300 transition-colors hover:text-zinc-50"
                onClick={closeMobileMenu}
              >
                {t('nav.login')}
              </Link>
              <Link
                href="/register?type=maker"
                className="inline-flex items-center gap-1.5 rounded-lg border border-brand-line px-4 py-1.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-brand-fill-soft"
                onClick={closeMobileMenu}
              >
                {t('nav.start_selling')}
                <span aria-hidden="true">→</span>
              </Link>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
