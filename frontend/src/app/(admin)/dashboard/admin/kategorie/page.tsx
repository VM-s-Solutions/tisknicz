import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { getAdminCategories } from '@/lib/api-client-helpers/admin-categories';
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

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <header>
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.categories.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.categories.subtitle')}
          </p>
        </header>

        <CreateCategoryForm />

        {result.success ? (
          result.value.items.length === 0 ? (
            <p className="text-sm text-zinc-400">{t('dashboard.admin.categories.list.empty')}</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {result.value.items.map((item) => (
                <li key={item.id}>
                  <CategoryRow item={item} />
                </li>
              ))}
            </ul>
          )
        ) : (
          <Alert variant="error">{t('dashboard.admin.categories.list.error')}</Alert>
        )}
      </div>
    </section>
  );
}
