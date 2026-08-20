import Link from 'next/link';
import type { ReactNode } from 'react';
import { Icon } from '@/components/ui/icon';
import type { Audience } from '@/lib/auth';
import { continueHref } from '@/lib/auth';
import type { DisplaySession } from '@/lib/auth/display-session';
import { t, type MessageKey } from '@/lib/i18n';

const ROLE_KEYS: Record<Audience, MessageKey> = {
  customer: 'auth.signedIn.role.customer',
  maker: 'auth.signedIn.role.maker',
  admin: 'auth.signedIn.role.admin',
};

interface AlreadySignedInProps {
  readonly session: DisplaySession;
  /** Validated (path-only) `?redirect=` target, or null. */
  readonly redirect: string | null;
  /** Sign-out affordance — a Client Component slot, so this stays server-rendered. */
  readonly switchAccount: ReactNode;
}

/**
 * Shown on /login when the visitor already holds a live session.
 *
 * Two bugs collapse into this one surface:
 *
 * 1. A signed-in maker who pressed "Objednat" was sent to /login by the
 *    checkout guard. Their account is bound to the maker audience
 *    (`User.MatchesAudience`), so logging in again just re-issued a
 *    maker JWT and bounced them straight back — an endless login screen.
 * 2. Browser Back from anywhere landed on that same /login entry, which
 *    still rendered a login form to an already-authenticated user.
 *
 * The panel never auto-redirects: a hard redirect here could ping-pong
 * with a page guard whose own auth check disagrees (display session is
 * decoded from the cookie, the API is authoritative). The user picks.
 */
export function AlreadySignedIn({ session, redirect, switchAccount }: AlreadySignedInProps) {
  const target = continueHref(session.audience, redirect);

  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-start gap-3 rounded-lg border border-zinc-800 bg-surface-card px-4 py-3">
        <span className="mt-0.5 text-brand-400">
          <Icon name="user" size={16} />
        </span>
        <div className="min-w-0">
          <p className="break-words text-sm font-medium text-zinc-100">
            {t('auth.signedIn.as', { email: session.email })}
          </p>
          <p className="mt-0.5 text-sm text-zinc-400">{t(ROLE_KEYS[session.audience])}</p>
        </div>
      </div>

      <Link
        href={target}
        className="inline-flex items-center justify-center gap-2 rounded-lg border border-brand-500/60 px-4 py-2 text-sm font-semibold tracking-wide text-brand-300 transition-colors duration-150 hover:border-brand-400 hover:bg-brand-500/10 hover:text-brand-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/60 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
      >
        {t('auth.signedIn.continue')}
        <span aria-hidden="true">
          <Icon name="arrowRight" size={16} />
        </span>
      </Link>

      {switchAccount}
    </div>
  );
}
