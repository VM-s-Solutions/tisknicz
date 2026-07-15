'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { createCategory } from '@/lib/api-client-helpers/admin-categories';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Create-category form (T-0119 / US-admin-0013 AC-1). The slug is
 * optional — the backend derives a diacritics-stripped one from the
 * name when omitted. Country is fixed to CZ at launch (the category
 * table is per-country reference data; a country picker lands with the
 * first non-CZ market). The profanity screen and slug-uniqueness gate
 * run server-side; their typed error codes resolve to Czech messages.
 */
export function CreateCategoryForm() {
  const router = useRouter();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [sortOrder, setSortOrder] = useState('100');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    setSuccess(null);

    const parsedSortOrder = Number.parseInt(sortOrder, 10);
    const result = await createCategory({
      name: name.trim(),
      slug: slug.trim() || undefined,
      description: description.trim() || undefined,
      sortOrder: Number.isFinite(parsedSortOrder) ? parsedSortOrder : 100,
      countryCode: 'CZ',
    });

    if (result.success) {
      setSuccess(t('dashboard.admin.categories.form.success', { slug: result.value.slug }));
      setName('');
      setSlug('');
      setDescription('');
      setSortOrder('100');
      router.refresh();
    } else {
      setError(resolveErrorMessage(result.error));
    }
    setSubmitting(false);
  }

  return (
    <Card className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold text-white">
        {t('dashboard.admin.categories.form.title')}
      </h2>
      {error ? <Alert variant="error">{error}</Alert> : null}
      {success ? <Alert variant="success">{success}</Alert> : null}
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            label={t('dashboard.admin.categories.form.name')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            disabled={submitting}
          />
          <Input
            label={t('dashboard.admin.categories.form.slug')}
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder={t('dashboard.admin.categories.form.slug_hint')}
            disabled={submitting}
          />
        </div>
        <Textarea
          label={t('dashboard.admin.categories.form.description')}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          disabled={submitting}
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            label={t('dashboard.admin.categories.form.sortOrder')}
            type="number"
            value={sortOrder}
            onChange={(e) => setSortOrder(e.target.value)}
            disabled={submitting}
          />
        </div>
        <div>
          <Button type="submit" loading={submitting} disabled={!name.trim() || submitting}>
            {t('dashboard.admin.categories.form.submit')}
          </Button>
        </div>
      </form>
    </Card>
  );
}
