'use client';

import { usePathname, useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

/**
 * URL-state search box for the admin makers list (T-0119b). One term —
 * company-name partial or exact IČO (the backend decides which). Pushes
 * `?search=` and resets the page param (T-0087a URL-state precedent).
 */
export function MakerSearchForm({ initialSearch }: { readonly initialSearch: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const [search, setSearch] = useState(initialSearch);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const params = new URLSearchParams();
    if (search.trim()) params.set('search', search.trim());
    router.replace(params.size > 0 ? `${pathname}?${params.toString()}` : pathname, {
      scroll: false,
    });
  }

  return (
    <form onSubmit={handleSubmit} className="flex items-end gap-2" noValidate>
      <div className="flex-1">
        <Input
          icon="search"
          label={t('dashboard.admin.ops.makers.list.searchLabel')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('dashboard.admin.ops.makers.list.searchPlaceholder')}
          autoComplete="off"
        />
      </div>
      <Button type="submit" variant="secondary">
        {t('dashboard.admin.ops.makers.list.searchSubmit')}
      </Button>
    </form>
  );
}
