import type { ReactNode } from 'react';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';

/**
 * Fullscreen auth layout: the shared public navbar and footer frame a
 * viewport-filling main area that centers the auth content (login,
 * register, magic, verify, reset). Ambient brand glows keep it aligned
 * with the landing hero without any card chrome.
 */
export default async function AuthLayout({ children }: { children: ReactNode }) {
  const session = await getDisplaySession();
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-zinc-100">
      <PublicNavbar session={session} />
      <main className="flex flex-1 items-center justify-center px-4 py-12 sm:px-6">
        <div className="w-full max-w-xl">{children}</div>
      </main>
      <PublicFooter />
    </div>
  );
}
