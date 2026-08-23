'use client';

import { useRouter } from 'next/navigation';
import { useTransition } from 'react';
import { t } from '@/lib/i18n';

/**
 * Retry that actually retries (T-0170, audit PUB-M3): error surfaces used
 * to render "Zkusit znovu" as a `<Link>` to the bare route, which dropped
 * the user's filters/page — and self-links can no-op in the client
 * router. `router.refresh()` re-runs the CURRENT URL's server render.
 */
export function RefreshButton({ label }: { readonly label?: string }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  return (
    <button
      type="button"
      onClick={() => startTransition(() => router.refresh())}
      disabled={pending}
      aria-busy={pending}
      className="inline-flex w-fit items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2 text-sm font-medium text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300 disabled:cursor-wait disabled:opacity-60"
    >
      {pending ? t('common.loading') : (label ?? t('common.retry'))}
    </button>
  );
}
