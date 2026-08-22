'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { SaveButton, type SaveState } from '@/components/ui/save-button';
import { Textarea } from '@/components/ui/textarea';
import {
  createProduct,
  FULFILLMENT_TYPES,
  type FulfillmentType,
  FulfillmentTypeValues,
  type MakerProductDetail,
  PRICE_TYPES,
  type PriceType,
  PriceTypeValues,
  updateProduct,
} from '@/lib/api-client-helpers/maker-products';
import { t, type MessageKey } from '@/lib/i18n';

interface ProductFormProps {
  readonly mode: 'create' | 'edit';
  /** Prefill values for the edit mode; undefined for create. */
  readonly initial?: MakerProductDetail;
  /**
   * Category picker options resolved server-side by the wrapping page
   * (data-driven since T-0119, static fallback). `value` is the
   * category ID — what `Product.CategoryId` references.
   */
  readonly categoryOptions: readonly { value: string; label: string }[];
}

const PRICE_TYPE_LABEL_KEYS: Record<PriceType, MessageKey> = {
  [PriceTypeValues.Fixed]: 'dashboard.maker.products.form.price_type.Fixed',
  [PriceTypeValues.From]: 'dashboard.maker.products.form.price_type.From',
  [PriceTypeValues.OnRequest]: 'dashboard.maker.products.form.price_type.OnRequest',
};

// Shared with the product-detail badge and checkout notice (T-0144).
const FULFILLMENT_TYPE_LABEL_KEYS: Record<FulfillmentType, MessageKey> = {
  [FulfillmentTypeValues.MadeToOrder]: 'product.fulfillmentType.MadeToOrder',
  [FulfillmentTypeValues.InStock]: 'product.fulfillmentType.InStock',
};

/** The persisted truth the dirty check compares against. */
interface FormSnapshot {
  readonly title: string;
  readonly description: string;
  readonly categoryId: string;
  readonly priceType: PriceType;
  readonly fulfillmentType: FulfillmentType;
  readonly priceKc: string;
  readonly weightGrams: string;
}

function snapshotFrom(initial: MakerProductDetail | undefined): FormSnapshot {
  return {
    title: initial?.title ?? '',
    description: initial?.description ?? '',
    categoryId: initial?.categoryId ?? '',
    priceType: (initial?.priceType as PriceType | undefined) ?? PriceTypeValues.Fixed,
    fulfillmentType:
      (initial?.fulfillmentType as FulfillmentType | undefined) ?? FulfillmentTypeValues.MadeToOrder,
    // Initial Kč uses Math.trunc to mirror lib/money/formatter.ts's
    // formatCzk (whole-CZK display, haléře dropped). Math.round here
    // would silently bump prices ending in ≥50 haléř upward — and an
    // unedited submit would persist the rounded value back to the
    // backend. T-0049 Copilot round-6 M2.
    priceKc: initial ? String(Math.trunc(initial.priceAmountMinor / 100)) : '',
    weightGrams: initial ? String(initial.weightGrams) : '',
  };
}

/**
 * DOM ids of the form's inputs in visual order — the scroll-to-error
 * pass (T-0174, audit MAKER-M1: a failed submit at the bottom of a long
 * form produced no in-viewport change) walks this list and brings the
 * first errored field into view. Keys match the camelCase field-error
 * names produced by `applyError`.
 */
const FIELD_ANCHORS: readonly (readonly [field: string, elementId: string])[] = [
  ['title', 'product-title'],
  ['categoryId', 'product-category'],
  ['fulfillmentType', 'product-fulfillment-type'],
  ['description', 'product-description'],
  ['priceType', 'product-price-type'],
  ['priceAmountMinor', 'product-price'],
  ['weightGrams', 'product-weight'],
];

/**
 * Shared product editor used by the create and edit routes (T-0049
 * AC-4 / AC-5 / AC-6; feedback reworked in T-0174). The form has no
 * client-side validation rules — the backend's FluentValidation is the
 * source of truth, and field-level errors surface inline via the
 * <c>fields</c> map on <c>ApiError</c> (T-0049 AC-5).
 *
 * <para>
 * Edit mode submits through the shared <c>SaveButton</c> with dirty
 * tracking, so save confirmation happens at the button the maker just
 * pressed (the old top-of-form success alert rendered off-viewport on
 * long forms and never cleared). Unsaved changes arm a
 * <c>beforeunload</c> guard in both modes (audit MAKER-M2).
 * </para>
 *
 * <para>
 * Price is shown to the maker in Kč; the backend stores
 * <c>priceAmountMinor</c> as haléře (Kč × 100). The submit handler
 * does the multiplication.
 * </para>
 */
export function ProductForm({ mode, initial, categoryOptions }: ProductFormProps) {
  const router = useRouter();

  // Form state. Init from `initial` in edit mode; otherwise empty.
  const [persisted, setPersisted] = useState<FormSnapshot>(() => snapshotFrom(initial));
  const [title, setTitle] = useState(persisted.title);
  const [description, setDescription] = useState(persisted.description);
  const [categoryId, setCategoryId] = useState(persisted.categoryId);
  const [priceType, setPriceType] = useState<PriceType>(persisted.priceType);
  // Defaults to "Na zakázku" — the form's explicit default selection
  // (AC-1), matching the platform's dominant use case (T-0144).
  const [fulfillmentType, setFulfillmentType] = useState<FulfillmentType>(persisted.fulfillmentType);
  const [priceKc, setPriceKc] = useState<string>(persisted.priceKc);
  const [weightGrams, setWeightGrams] = useState<string>(persisted.weightGrams);

  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [topError, setTopError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Readonly<Record<string, string>>>({});
  const formRef = useRef<HTMLFormElement | null>(null);
  // Element id to scroll+focus once the errored render committed. Focus
  // must NOT run inside `applyError` — at that moment the inputs are
  // still disabled from the in-flight submit and `.focus()` would no-op.
  const pendingFocusIdRef = useRef<string | null>(null);

  const submitting = saveState === 'saving';
  const current: FormSnapshot = {
    title,
    description,
    categoryId,
    priceType,
    fulfillmentType,
    priceKc,
    weightGrams,
  };
  const dirty = (Object.keys(current) as (keyof FormSnapshot)[]).some(
    (key) => current[key] !== persisted[key],
  );

  // Unsaved edits are silently lost on refresh/close (audit MAKER-M2) —
  // arm the native guard only while the form is actually dirty.
  useEffect(() => {
    if (!dirty || submitting) return;
    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', guard);
    return () => window.removeEventListener('beforeunload', guard);
  }, [dirty, submitting]);

  const priceTypeOptions = PRICE_TYPES.map((value) => ({
    value,
    label: t(PRICE_TYPE_LABEL_KEYS[value]),
  }));

  const fulfillmentTypeOptions = FULFILLMENT_TYPES.map((value) => ({
    value,
    label: t(FULFILLMENT_TYPE_LABEL_KEYS[value]),
  }));

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setTopError(null);
    setFieldErrors({});
    setSaveState('saving');

    // Parse numeric inputs once. NaN protection only — the backend
    // validates the real rules (range, positivity, etc.).
    const isOnRequest = priceType === PriceTypeValues.OnRequest;
    const priceKcParsed = Number.parseFloat(priceKc);
    const weightParsed = Number.parseInt(weightGrams, 10);
    // OnRequest pins priceAmountMinor to 0 regardless of what the price
    // input currently holds — the input is disabled in this mode, so a
    // stale value left over from a Fixed/From session would otherwise
    // ride the wire invisibly. Domain rule (Product.Create): amount=0
    // requires OnRequest, so this is the safe canonical shape. T-0049
    // Copilot review M2.
    const priceAmountMinor = isOnRequest
      ? 0
      : Number.isFinite(priceKcParsed)
        ? Math.round(priceKcParsed * 100)
        : 0;
    const weight = Number.isFinite(weightParsed) ? weightParsed : 0;

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
      fulfillmentType,
      weightGrams: weight,
    };

    if (mode === 'create') {
      const result = await createProduct(payload);
      if (!result.success) {
        setSaveState('idle');
        applyError(result.error.type, result.error.fields);
        return;
      }
      // Marking the just-created values as persisted disarms the
      // beforeunload guard before the client-side navigation below.
      setPersisted(current);
      setSaveState('saved');
      // `?created=1` drives the created-confirmation on the edit page
      // (audit MAKER-M7 — the handoff used to be silent).
      router.push(
        `/dashboard/maker/produkty/${encodeURIComponent(result.value.id)}?created=1`,
      );
      return;
    }

    if (!initial) {
      // Edit mode requires `initial`; defensive guard for an impossible
      // state. No i18n key — this is a developer-time invariant.
      setSaveState('idle');
      setTopError(t('dashboard.maker.products.form.error.generic'));
      return;
    }
    const result = await updateProduct(initial.productId, payload);
    if (!result.success) {
      setSaveState('idle');
      applyError(result.error.type, result.error.fields);
      return;
    }
    setPersisted(current);
    setSaveState('saved');
    router.refresh();
  }

  /**
   * Map an <c>ApiError</c> into the form's error state. Validation
   * errors with a <c>fields</c> map surface inline next to each
   * matching input (AC-5); everything else surfaces as a top-of-form
   * alert with generic i18n copy (we deliberately do NOT render the
   * raw error message — it might not be Czech-safe or audience-safe).
   * Either way the first affected element is scrolled into view — the
   * form is long and the submit sits at the bottom (audit MAKER-M1).
   */
  function applyError(
    errorType: string,
    fields: Readonly<Record<string, readonly string[]>> | undefined,
  ) {
    if (errorType === 'Validation' && fields && Object.keys(fields).length > 0) {
      const mapped: Record<string, string> = {};
      for (const [name, messages] of Object.entries(fields)) {
        // The strings here are display copy (the backend's message,
        // falling back to its code when the message is empty — see
        // collectValidationFields in lib/runtime/api-fetch.ts). Take
        // the first per field; the dashboard surfaces one error per
        // input.
        const first = messages[0];
        if (first) {
          // FluentValidation emits property names as PascalCase
          // (CategoryId, Title, ...); the form's state keys are
          // camelCase (matching the request DTO). Normalise the first
          // character so CategoryId → categoryId, etc.
          const normalized = name.charAt(0).toLowerCase() + name.slice(1);
          mapped[normalized] = first;
        }
      }
      setFieldErrors(mapped);
      setTopError(t('dashboard.maker.products.form.error.validation_summary'));
      const firstAnchor = FIELD_ANCHORS.find(([field]) => field in mapped);
      pendingFocusIdRef.current = firstAnchor ? firstAnchor[1] : null;
      return;
    }
    setTopError(t('dashboard.maker.products.form.error.generic'));
    pendingFocusIdRef.current = null;
  }

  // After an errored render commits (inputs re-enabled), bring the first
  // errored input into view and focus it; a generic failure scrolls the
  // top alert into view instead.
  useEffect(() => {
    if (!topError) return;
    const targetId = pendingFocusIdRef.current;
    pendingFocusIdRef.current = null;
    if (!targetId) {
      formRef.current?.scrollIntoView({ block: 'start', behavior: 'smooth' });
      return;
    }
    const element = document.getElementById(targetId);
    if (!element) return;
    element.scrollIntoView({ block: 'center', behavior: 'smooth' });
    element.focus({ preventScroll: true });
  }, [topError, fieldErrors]);

  const isOnRequest = priceType === PriceTypeValues.OnRequest;

  return (
    <form ref={formRef} onSubmit={handleSubmit} className="flex flex-col gap-6" noValidate>
      {topError ? <Alert variant="error">{topError}</Alert> : null}

      <Card variant="elevated" padding="lg">
        <section className="flex flex-col gap-4">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-9 w-9">
              <Icon name="edit" size={16} />
            </span>
            <h2 className="text-lg font-semibold text-white">
              {t('dashboard.maker.products.form.section_basic')}
            </h2>
          </div>
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
          <Dropdown
            id="product-category"
            label={t('dashboard.maker.products.form.field.category')}
            value={categoryId}
            onChange={setCategoryId}
            options={categoryOptions}
            placeholder={t('dashboard.maker.products.form.field.category_placeholder')}
            disabled={submitting}
            error={fieldErrors.categoryId}
          />
          <Dropdown
            id="product-fulfillment-type"
            label={t('dashboard.maker.products.form.field.fulfillment_type')}
            value={fulfillmentType}
            onChange={(value) => setFulfillmentType(value as FulfillmentType)}
            options={fulfillmentTypeOptions}
            disabled={submitting}
            error={fieldErrors.fulfillmentType}
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
      </Card>

      <Card variant="elevated" padding="lg">
        <section className="flex flex-col gap-4">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-9 w-9">
              <Icon name="creditCard" size={16} />
            </span>
            <h2 className="text-lg font-semibold text-white">
              {t('dashboard.maker.products.form.section_pricing')}
            </h2>
          </div>
          <Dropdown
            id="product-price-type"
            label={t('dashboard.maker.products.form.field.price_type')}
            value={priceType}
            onChange={(value) => setPriceType(value as PriceType)}
            options={priceTypeOptions}
            disabled={submitting}
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
      </Card>

      <div className="flex items-center justify-end gap-3">
        {mode === 'edit' ? (
          <SaveButton state={saveState} dirty={dirty} />
        ) : (
          <Button type="submit" loading={submitting} variant="primary">
            {!submitting ? <Icon name="plus" size={16} /> : null}
            {submitting
              ? t('dashboard.maker.products.form.submit.saving')
              : t('dashboard.maker.products.form.submit.create')}
          </Button>
        )}
      </div>
    </form>
  );
}
