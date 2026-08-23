import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

export default function NotFound() {
  return (
    <div className="flex min-h-[calc(100vh-64px)] items-center justify-center bg-surface-primary px-4">
      <div className="text-center">
        <p aria-hidden="true" className="text-7xl font-bold tracking-tight text-zinc-700 sm:text-8xl">
          404
        </p>
        <h1 className="mt-4 text-2xl font-bold tracking-tight text-zinc-50 sm:text-3xl">
          {t('notFound.title')}
        </h1>
        <p className="mt-3 text-base text-zinc-400">{t('notFound.body')}</p>
        <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
          <Link
            href="/"
            className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors duration-150 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
          >
            <Icon name="arrowLeft" size={16} />
            {t('notFound.back_home')}
          </Link>
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors duration-150 hover:border-brand-500/60 hover:text-brand-300"
          >
            {t('notFound.browse_catalog')}
          </Link>
        </div>
      </div>
    </div>
  );
}
