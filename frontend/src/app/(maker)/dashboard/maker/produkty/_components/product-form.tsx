'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import {
  createProduct,
  type MakerProductDetail,
  PRICE_TYPES,
  type PriceType,
  PriceTypeValues,
  updateProduct,
} from '@/lib/api-client-helpers/maker-products';
import { CATALOG_CATEGORIES } from '@/lib/catalog/categories';
import { t, type MessageKey } from '@/lib/i18n';

interface ProductFormProps {
  readonly mode: 'create' | 'edit';
  /** Prefill values for the edit mode; undefined for create. */
  readonly initial?: MakerProductDetail;
}

const PRICE_TYPE_LABEL_KEYS: Record<PriceType, MessageKey> = {
  [PriceTypeValues.Fixed]: 'dashboard.maker.products.form.price_type.Fixed',
  [PriceTypeValues.From]: 'dashboard.maker.products.form.price_type.From',
  [PriceTypeValues.OnRequest]: 'dashboard.maker.products.form.price_type.OnRequest',
};

/**
 * Shared product editor used by the create and edit routes (T-0049
 * AC-4 / AC-5 / AC-6). The form has no client-side validation rules —
 * the backend's FluentValidation is the source of truth, and field-
 * level errors surface inline via the <c>fields</c> map on
 * <c>ApiError</c> (T-0049 AC-5). Native HTML <c>required</c> and
 * <c>min</c> are kept for UX-only sanity checks; they don't replace
 * the backend rules.
 *
 * <para>
 * Price is shown to the maker in Kč; the backend stores
 * <c>priceAmountMinor</c> as haléře (Kč × 100). The submit handler
 * does the multiplication.
 * </para>
 */
export function ProductForm({ mode, initial }: ProductFormProps) {
  const router = useRouter();

  // Form state. Init from `initial` in edit mode; otherwise empty.
  const [title, setTitle] = useState(initial?.title ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [categoryId, setCategoryId] = useState(initial?.categoryId ?? '');
  const [priceType, setPriceType] = useState<PriceType>(
    (initial?.priceType as PriceType | undefined) ?? PriceTypeValues.Fixed,
  );
  const [priceKc, setPriceKc] = useState<string>(
    initial ? String(Math.round(initial.priceAmountMinor / 100)) : '',
  );
  const [weightGrams, setWeightGrams] = useState<string>(
    initial ? String(initial.weightGrams) : '',
  );

  const [submitting, setSubmitting] = useState(false);
  const [topError, setTopError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Readonly<Record<string, string>>>({});
  const [savedFlash, setSavedFlash] = useState(false);

  const categoryOptions = CATALOG_CATEGORIES.map((c) => ({
    value: c.slug,
    label: t(c.labelKey),
  }));

  const priceTypeOptions = PRICE_TYPES.map((value) => ({
    value,
    label: t(PRICE_TYPE_LABEL_KEYS[value]),
  }));

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setTopError(null);
    setFieldErrors({});
    setSavedFlash(false);
    setSubmitting(true);

    // Parse numeric inputs once. NaN protection only — the backend
    // validates the real rules (range, positivity, etc.).
    const priceKcParsed = Number.parseFloat(priceKc);
    const weightParsed = Number.parseInt(weightGrams, 10);
    const priceAmountMinor = Number.isFinite(priceKcParsed)
      ? Math.round(priceKcParsed * 100)
      : 0;
    const weight = Number.isFinite(weightParsed) ? weightParsed : 0;

    // OnRequest products legitimately submit priceAmountMinor = 0 — the
    // domain rule (Product.Create) requires PriceType.OnRequest exactly
    // when the amount is zero, and an OnRequest product with any non-zero
    // amount is treated as an "informational from" price. The disabled-
    // input + zero-default flow here matches that. T-0049 review M2.
    //
    // `description: undefined` is intentional: JSON.stringify drops
    // undefined properties on objects, so the wire payload omits the
    // field entirely. Don't change to `null` — the backend's optional
    // string contract is "absent or non-empty", not "absent or null".
    const payload = {
      categoryId,
      title,
      description: description.trim() === '' ? undefined : description,
      priceAmountMinor,
      priceType,
      weightGrams: weight,
    };

    if (mode === 'create') {
      const result = await createProduct(payload);
      setSubmitting(false);
      if (!result.success) {
        applyError(result.error.type, result.error.fields);
        return;
      }
      router.push(`/dashboard/maker/produkty/${encodeURIComponent(result.value.id)}`);
      return;
    }

    if (!initial) {
      // Edit mode requires `initial`; defensive guard for an impossible
      // state. No i18n key — this is a developer-time invariant.
      setSubmitting(false);
      setTopError(t('dashboard.maker.products.form.error.generic'));
      return;
    }
    const result = await updateProduct(initial.productId, payload);
    setSubmitting(false);
    if (!result.success) {
      applyError(result.error.type, result.error.fields);
      return;
    }
    setSavedFlash(true);
    router.refresh();
  }

  /**
   * Map an <c>ApiError</c> into the form's error state. Validation
   * errors with a <c>fields</c> map surface inline next to each
   * matching input (AC-5); everything else surfaces as a top-of-form
   * alert with generic i18n copy (we deliberately do NOT render the
   * raw error message — it might not be Czech-safe or audience-safe).
   */
  function applyError(
    errorType: string,
    fields: Readonly<Record<string, readonly string[]>> | undefined,
  ) {
    if (errorType === 'Validation' && fields && Object.keys(fields).length > 0) {
      const mapped: Record<string, string> = {};
      for (const [name, codes] of Object.entries(fields)) {
        // Field names from the backend are camelCase (matches the
        // request DTO). Take the first code per field — the dashboard
        // displays one error per input.
        const first = codes[0];
        if (first) {
          // Normalize the first character to lowercase so the backend's
          // PascalCase property name (FluentValidation default) maps to
          // our state keys (CategoryId → categoryId, etc.).
          const normalized = name.charAt(0).toLowerCase() + name.slice(1);
          mapped[normalized] = first;
        }
      }
      setFieldErrors(mapped);
      setTopError(t('dashboard.maker.products.form.error.validation_summary'));
      return;
    }
    setTopError(t('dashboard.maker.products.form.error.generic'));
  }

  const isOnRequest = priceType === PriceTypeValues.OnRequest;

  return (
    <Card padding="lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-6" noValidate>
        {topError ? <Alert variant="error">{topError}</Alert> : null}
        {savedFlash ? (
          <Alert variant="success">{t('dashboard.maker.products.form.success.updated')}</Alert>
        ) : null}

        <section className="flex flex-col gap-4">
          <h2 className="text-lg font-semibold text-white">
            {t('dashboard.maker.products.form.section_basic')}
          </h2>
          <Input
            id="product-title"
            label={t('dashboard.maker.products.form.field.title')}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            disabled={submitting}
            required
            maxLength={200}
            error={fieldErrors.title}
          />
          <Select
            id="product-category"
            label={t('dashboard.maker.products.form.field.category')}
            value={categoryId}
            onChange={(e) => setCategoryId(e.target.value)}
            options={categoryOptions}
            placeholder={t('dashboard.maker.products.form.field.category_placeholder')}
            disabled={submitting}
            required
            error={fieldErrors.categoryId}
          />
          <div className="flex flex-col gap-1.5">
            <Textarea
              id="product-description"
              label={t('dashboard.maker.products.form.field.description')}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              disabled={submitting}
              rows={5}
              maxLength={5000}
              error={fieldErrors.description}
            />
            <p className="text-xs text-zinc-500">
              {t('dashboard.maker.products.form.field.description_help')}
            </p>
          </div>
        </section>

        <section className="flex flex-col gap-4">
          <h2 className="text-lg font-semibold text-white">
            {t('dashboard.maker.products.form.section_pricing')}
          </h2>
          <Select
            id="product-price-type"
            label={t('dashboard.maker.products.form.field.price_type')}
            value={priceType}
            onChange={(e) => setPriceType(e.target.value as PriceType)}
            options={priceTypeOptions}
            disabled={submitting}
            required
            error={fieldErrors.priceType}
          />
          <div className="flex flex-col gap-1.5">
            <Input
              id="product-price"
              type="number"
              inputMode="decimal"
              step="1"
              min="0"
              label={t('dashboard.maker.products.form.field.price_amount')}
              value={priceKc}
              onChange={(e) => setPriceKc(e.target.value)}
              disabled={submitting || isOnRequest}
              required={!isOnRequest}
              error={fieldErrors.priceAmountMinor}
            />
            <p className="text-xs text-zinc-500">
              {t('dashboard.maker.products.form.field.price_amount_help')}
            </p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Input
              id="product-weight"
              type="number"
              inputMode="numeric"
              step="1"
              min="0"
              label={t('dashboard.maker.products.form.field.weight')}
              value={weightGrams}
              onChange={(e) => setWeightGrams(e.target.value)}
              disabled={submitting}
              required
              error={fieldErrors.weightGrams}
            />
            <p className="text-xs text-zinc-500">
              {t('dashboard.maker.products.form.field.weight_help')}
            </p>
          </div>
        </section>

        <div className="flex items-center justify-end gap-3 pt-2">
          <Button type="submit" loading={submitting} variant="primary">
            {submitting
              ? t('dashboard.maker.products.form.submit.saving')
              : mode === 'create'
                ? t('dashboard.maker.products.form.submit.create')
                : t('dashboard.maker.products.form.submit.update')}
          </Button>
        </div>
      </form>
    </Card>
  );
}
