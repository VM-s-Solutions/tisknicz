import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { getAdminCategories } from '@/lib/api-client-helpers/admin-categories';
import { redirect } from 'next/navigation';
import { t } from '@/lib/i18n';
import { CategoryRow } from './category-row';
import { CreateCategoryForm } from './create-category-form';

/**
 * Admin category management (T-0119 / US-admin-0013). Server Component,
 * `force-dynamic` (the dashboard always reflects the latest backend
 * state). Lists EVERY category including deactivated rows — the list is
 * the one surface where an admin sees soft-deleted taxonomy — with the
 * create form on top and per-row rename/deactivate actions.
 *
 * Names, slugs and descriptions are screened server-side against the
 * profanity blocklist (`category.nameNotAllowed`); the slug is derived
 * from the name when omitted and NEVER changes on rename
 * (US-admin-0013 AC-2 — public URLs and product FKs survive).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.categories.metadata.title'),
    description: t('dashboard.admin.categories.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

export default async function AdminCategoriesPage() {
  const result = await getAdminCategories();

  // T-0175 (audit ADM-H3): parity with every other admin route.
  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent('/dashboard/admin/kategorie')}`);
  }

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <header>
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="tag" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.categories.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.categories.subtitle')}
          </p>
        </header>

        <CreateCategoryForm />

        {result.success ? (
          result.value.items.length === 0 ? (
            <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed border-zinc-800 bg-surface-card px-6 py-12 text-center">
              <span className="icon-tile h-12 w-12" aria-hidden="true">
                <Icon name="tag" size={20} />
              </span>
              <p className="text-sm text-zinc-400">{t('dashboard.admin.categories.list.empty')}</p>
            </div>
          ) : (
            <div className="rounded-xl border border-zinc-800 bg-surface-card">
              <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
                <div className="flex items-center gap-2.5">
                  <h2 className="text-sm font-semibold text-zinc-100">
                    {t('dashboard.admin.categories.title')}
                  </h2>
                  <Badge dot={false}>{result.value.items.length}</Badge>
                </div>
              </div>
              <ul className="divide-y divide-zinc-800">
                {result.value.items.map((item) => (
                  <li key={item.id}>
                    <CategoryRow item={item} />
                  </li>
                ))}
              </ul>
            </div>
          )
        ) : (
          <Alert variant="error">{t('dashboard.admin.categories.list.error')}</Alert>
        )}
      </div>
    </section>
  );
}
