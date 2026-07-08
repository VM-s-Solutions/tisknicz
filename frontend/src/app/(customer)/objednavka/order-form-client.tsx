'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { ZasilkovnaWidget } from '@/components/shared/zasilkovna-widget';
import {
  ShippingMethod,
  createOrder,
  uploadOrderAttachment,
} from '@/lib/api-client-helpers/orders-client';
import type { PickupPointWidgetConfig } from '@/lib/api-client-helpers/shipping';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import {
  CZECH_PHONE_PATTERN,
  ORDER_CONTACT_EMAIL_MAX,
  ORDER_CONTACT_NAME_MAX,
  ORDER_CONTACT_NAME_MIN,
  ORDER_NOTES_MAX,
} from '@/lib/utils/validation';
import { AttachmentPicker, type CheckoutAttachment } from './attachment-picker';

/**
 * Checkout order form (T-0084a, US-customer-0010/0011). Owns form
 * state, the T-0063 validation mirrors (UX-only — backend
 * authoritative), the shipping-method radio + pickup-point state, the
 * `ApiError.fields` → inline-error mapping (PascalCase normalised to
 * camelCase per patterns.md B.17) and the locked submit orchestration:
 * mirror-validate → createOrder → sequential attachment uploads →
 * navigate to /objednavka/[id] (appending ?attachmentsFailed=N on
 * partial upload failures — the order page is the retry surface).
 */

interface PickupPoint {
  readonly id: string;
  readonly name: string;
}

export interface OrderFormClientProps {
  readonly productId: string;
  /** Prefill from the customer profile (editable per T-0084a §C). */
  readonly defaultEmail: string;
  readonly personalPickupEnabled: boolean;
  readonly pickupNote: string | null;
  readonly pickupCity: string;
  /** `null` when the SSR widget-config fetch failed — Zásilkovna degrades to disabled + retry. */
  readonly widgetConfig: PickupPointWidgetConfig | null;
  /**
   * Drives the withdrawal-right notice (T-0144, § 1837 písm. d) OZ) —
   * rendered above the submit action, before any payment redirect can
   * occur (AC-4/AC-5). Read from the same loaded product as
   * `product.priceType` (US-customer-0009 AC-4 precedent); no separate
   * fetch.
   */
  readonly fulfillmentType: 'MadeToOrder' | 'InStock';
}

/** UX mirror of the Czech-format email rule — backend `EmailAddress()` stays authoritative. */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function OrderFormClient({
  productId,
  defaultEmail,
  personalPickupEnabled,
  pickupNote,
  pickupCity,
  widgetConfig,
  fulfillmentType,
}: OrderFormClientProps) {
  const router = useRouter();

  const [customerName, setCustomerName] = useState('');
  const [customerEmail, setCustomerEmail] = useState(defaultEmail);
  const [customerPhone, setCustomerPhone] = useState('');
  const [customerNotes, setCustomerNotes] = useState('');

  const [widgetFailed, setWidgetFailed] = useState(false);
  const zasilkovnaAvailable = widgetConfig !== null && !widgetFailed;

  const [shippingMethod, setShippingMethod] = useState<ShippingMethod | null>(() => {
    if (widgetConfig !== null) return ShippingMethod.ZasilkovnaPickupPoint;
    if (personalPickupEnabled) return ShippingMethod.PersonalPickup;
    return null;
  });
  const [pickupPoint, setPickupPoint] = useState<PickupPoint | null>(null);

  const [attachments, setAttachments] = useState<readonly CheckoutAttachment[]>([]);
  const [uploadProgress, setUploadProgress] = useState<{ done: number; total: number } | null>(null);

  const [fieldErrors, setFieldErrors] = useState<Readonly<Record<string, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  const noShippingSelectable = !zasilkovnaAvailable && !personalPickupEnabled;

  function handleWidgetError() {
    setWidgetFailed(true);
    setPickupPoint(null);
    setShippingMethod((current) =>
      current === ShippingMethod.ZasilkovnaPickupPoint
        ? personalPickupEnabled
          ? ShippingMethod.PersonalPickup
          : null
        : current,
    );
  }

  function handleWidgetRetry() {
    if (widgetConfig === null) {
      // The SSR config fetch failed — a server re-render is the only
      // way to re-attempt it (the config is an SSR prop).
      router.refresh();
      return;
    }
    // Runtime/script failure: the script loader cleared its cached
    // promise on error, so the next "choose point" click re-attempts
    // the injection (T-0084a AC-6).
    setWidgetFailed(false);
    setShippingMethod((current) => current ?? ShippingMethod.ZasilkovnaPickupPoint);
  }

  function validateMirrors(): Record<string, string> {
    const errors: Record<string, string> = {};
    const name = customerName.trim();
    if (name.length < ORDER_CONTACT_NAME_MIN || name.length > ORDER_CONTACT_NAME_MAX) {
      errors.customerName = t('checkout.validation.name');
    }
    const email = customerEmail.trim();
    if (!EMAIL_PATTERN.test(email) || email.length > ORDER_CONTACT_EMAIL_MAX) {
      errors.customerEmail = t('checkout.validation.email');
    }
    if (!CZECH_PHONE_PATTERN.test(customerPhone.trim())) {
      errors.customerPhone = t('checkout.validation.phone');
    }
    if (customerNotes.length > ORDER_NOTES_MAX) {
      errors.customerNotes = t('checkout.validation.notes');
    }
    if (shippingMethod === ShippingMethod.ZasilkovnaPickupPoint && pickupPoint === null) {
      errors.zasilkovnaPickupPointId = t('checkout.pickupPoint.required');
    }
    return errors;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // Step 2 — in-flight guard (T-0063 Q2 lock: disabled button + guard,
    // no backend Idempotency-Key). Ref-based so a rapid double-click
    // before the re-render still fires exactly one POST (AC-8).
    if (inFlightRef.current) return;

    // Step 1 — mirror validation; any failure stops before the network.
    setFormError(null);
    if (shippingMethod === null) {
      setFormError(t('checkout.shipping.unavailable'));
      return;
    }
    const errors = validateMirrors();
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) return;

    inFlightRef.current = true;
    setSubmitting(true);

    // Step 3 — create the order. Quantity is fixed at 1 (T-0061 invariant).
    const result = await createOrder({
      productId,
      quantity: 1,
      shippingMethod,
      zasilkovnaPickupPointId:
        shippingMethod === ShippingMethod.ZasilkovnaPickupPoint ? pickupPoint?.id : undefined,
      customerName: customerName.trim(),
      customerEmail: customerEmail.trim(),
      customerPhone: customerPhone.trim(),
      customerNotes: customerNotes.trim() === '' ? undefined : customerNotes.trim(),
    });

    if (!result.success) {
      if (result.error.type === 'Unauthorized') {
        router.push(buildLoginUrl(productId));
        return;
      }
      if (result.error.fields) {
        setFieldErrors(normalizeFieldErrors(result.error.fields));
      }
      setFormError(buildFormError(result.error));
      inFlightRef.current = false;
      setSubmitting(false);
      return;
    }

    // Step 4 — upload every picked file sequentially; never abort the
    // loop on failure (Q2 lock — the order page is the retry surface).
    const orderId = result.value.orderId;
    const files = attachments;
    let failedCount = 0;
    setUploadProgress({ done: 0, total: files.length });
    for (let index = 0; index < files.length; index++) {
      setAttachments((prev) =>
        prev.map((item, i) => (i === index ? { ...item, status: 'uploading' } : item)),
      );
      const upload = await uploadOrderAttachment(orderId, files[index].file);
      if (!upload.success) failedCount += 1;
      setAttachments((prev) =>
        prev.map((item, i) =>
          i === index ? { ...item, status: upload.success ? 'done' : 'failed' } : item,
        ),
      );
      setUploadProgress({ done: index + 1, total: files.length });
    }

    // Step 5 — navigate. The button stays disabled through navigation —
    // the create already succeeded; re-enabling invites a duplicate.
    const suffix = failedCount > 0 ? `?attachmentsFailed=${failedCount}` : '';
    router.push(`/objednavka/${encodeURIComponent(orderId)}${suffix}`);
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-6" noValidate>
      {formError ? <Alert variant="error">{formError}</Alert> : null}
      {noShippingSelectable ? (
        <Alert variant="error">{t('checkout.shipping.unavailable')}</Alert>
      ) : null}

      <Card padding="md" className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-4">
          <legend className="text-lg font-semibold text-white">
            {t('checkout.contact.legend')}
          </legend>
          <Input
            type="text"
            label={t('checkout.contact.name')}
            placeholder={t('checkout.contact.namePlaceholder')}
            value={customerName}
            onChange={(e) => setCustomerName(e.target.value)}
            error={fieldErrors.customerName}
            autoComplete="name"
            disabled={submitting}
            required
          />
          <Input
            type="email"
            label={t('checkout.contact.email')}
            value={customerEmail}
            onChange={(e) => setCustomerEmail(e.target.value)}
            error={fieldErrors.customerEmail}
            autoComplete="email"
            disabled={submitting}
            required
          />
          <Input
            type="tel"
            label={t('checkout.contact.phone')}
            placeholder={t('checkout.contact.phonePlaceholder')}
            value={customerPhone}
            onChange={(e) => setCustomerPhone(e.target.value)}
            error={fieldErrors.customerPhone}
            autoComplete="tel"
            disabled={submitting}
            required
          />
          <div className="flex flex-col gap-1.5">
            <Textarea
              label={t('checkout.contact.notes')}
              value={customerNotes}
              onChange={(e) => setCustomerNotes(e.target.value)}
              error={fieldErrors.customerNotes}
              rows={4}
              maxLength={ORDER_NOTES_MAX}
              disabled={submitting}
            />
            <p className="text-xs text-zinc-500">
              {t('checkout.contact.notesCounter', {
                count: customerNotes.length,
                max: ORDER_NOTES_MAX,
              })}
            </p>
          </div>
        </fieldset>
      </Card>

      <Card padding="md" className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-3">
          <legend className="text-lg font-semibold text-white">
            {t('checkout.shipping.legend')}
          </legend>

          <label
            className={`flex items-start gap-3 rounded-xl border px-4 py-3 transition-colors ${
              shippingMethod === ShippingMethod.ZasilkovnaPickupPoint
                ? 'border-brand-400 bg-brand-400/5'
                : 'border-zinc-800'
            } ${zasilkovnaAvailable && !submitting ? 'cursor-pointer hover:border-zinc-600' : 'cursor-not-allowed opacity-60'}`}
          >
            <input
              type="radio"
              name="shippingMethod"
              className="mt-1 accent-brand-400"
              checked={shippingMethod === ShippingMethod.ZasilkovnaPickupPoint}
              onChange={() => setShippingMethod(ShippingMethod.ZasilkovnaPickupPoint)}
              disabled={!zasilkovnaAvailable || submitting}
            />
            <span className="flex flex-col gap-2">
              <span className="text-sm font-medium text-zinc-100">
                {t('checkout.shipping.zasilkovna')}
              </span>
              {shippingMethod === ShippingMethod.ZasilkovnaPickupPoint && zasilkovnaAvailable ? (
                <span className="flex flex-col gap-2">
                  {pickupPoint ? (
                    <span className="text-sm text-zinc-300">
                      {t('checkout.pickupPoint.chosen', { name: pickupPoint.name })}
                    </span>
                  ) : null}
                  {widgetConfig ? (
                    <span>
                      <ZasilkovnaWidget
                        scriptUrl={widgetConfig.scriptUrl}
                        publicKey={widgetConfig.publicKey}
                        options={widgetConfig.options}
                        onPick={(point) => {
                          setPickupPoint(point);
                          setFieldErrors((prev) => {
                            const next = { ...prev };
                            delete next.zasilkovnaPickupPointId;
                            return next;
                          });
                        }}
                        onError={handleWidgetError}
                        label={
                          pickupPoint
                            ? t('checkout.pickupPoint.change')
                            : t('checkout.pickupPoint.choose')
                        }
                      />
                    </span>
                  ) : null}
                  {fieldErrors.zasilkovnaPickupPointId ? (
                    <span className="text-sm text-error">
                      {fieldErrors.zasilkovnaPickupPointId}
                    </span>
                  ) : null}
                </span>
              ) : null}
            </span>
          </label>

          {!zasilkovnaAvailable ? (
            <Alert variant="warning">
              <div className="flex flex-col gap-2">
                <p>{t('checkout.widget.error')}</p>
                <Button type="button" variant="outline" size="sm" onClick={handleWidgetRetry}>
                  {t('checkout.widget.retry')}
                </Button>
              </div>
            </Alert>
          ) : null}

          <label
            title={personalPickupEnabled ? undefined : t('checkout.shipping.personalPickupDisabled')}
            className={`flex items-start gap-3 rounded-xl border px-4 py-3 transition-colors ${
              shippingMethod === ShippingMethod.PersonalPickup
                ? 'border-brand-400 bg-brand-400/5'
                : 'border-zinc-800'
            } ${personalPickupEnabled && !submitting ? 'cursor-pointer hover:border-zinc-600' : 'cursor-not-allowed opacity-60'}`}
          >
            <input
              type="radio"
              name="shippingMethod"
              className="mt-1 accent-brand-400"
              checked={shippingMethod === ShippingMethod.PersonalPickup}
              onChange={() => setShippingMethod(ShippingMethod.PersonalPickup)}
              disabled={!personalPickupEnabled || submitting}
            />
            <span className="flex flex-col gap-1">
              <span className="text-sm font-medium text-zinc-100">
                {t('checkout.shipping.personalPickup')}
              </span>
              {!personalPickupEnabled ? (
                <span className="text-xs text-zinc-500">
                  {t('checkout.shipping.personalPickupDisabled')}
                </span>
              ) : null}
              {shippingMethod === ShippingMethod.PersonalPickup && personalPickupEnabled ? (
                <span className="flex flex-col gap-1 text-sm text-zinc-400">
                  <span>{t('checkout.shipping.pickupInfo', { city: pickupCity })}</span>
                  {pickupNote ? <span className="whitespace-pre-line">{pickupNote}</span> : null}
                </span>
              ) : null}
            </span>
          </label>
        </fieldset>
      </Card>

      <Card padding="md">
        <AttachmentPicker
          attachments={attachments}
          onAdd={(files) =>
            setAttachments((prev) => [
              ...prev,
              ...files.map((file) => ({ file, status: 'pending' as const })),
            ])
          }
          onRemove={(index) => setAttachments((prev) => prev.filter((_, i) => i !== index))}
          disabled={submitting}
        />
      </Card>

      <WithdrawalNotice fulfillmentType={fulfillmentType} />

      <div className="flex flex-col gap-2">
        <Button type="submit" size="lg" loading={submitting} disabled={noShippingSelectable}>
          {submitting ? t('checkout.submitting') : t('checkout.submit')}
        </Button>
        {uploadProgress && uploadProgress.total > 0 ? (
          <p className="text-center text-sm text-zinc-400">
            {t('checkout.uploadProgress', {
              done: uploadProgress.done,
              total: uploadProgress.total,
            })}
          </p>
        ) : null}
      </div>
    </form>
  );
}

/**
 * Withdrawal-right notice (T-0144, US-customer-0021). Branches on the
 * ordered product's `fulfillmentType` — made-to-order goods are exempt
 * from the standard 14-day right of withdrawal (§ 1837 písm. d)
 * občanského zákoníku); in-stock goods carry the normal 14-day right.
 * Renders above the submit action so the customer is informed before
 * any payment redirect can occur (AC-4/AC-5).
 *
 * <para>
 * Both copy variants are interim placeholder text per the T-0130
 * legal-placeholder-lock pattern (same idiom as `/vop` and `/gdpr`) —
 * the final wording is gated behind the pre-existing Q-0030 launch-
 * blocker, which this ticket does not resolve (AC-7).
 * </para>
 */
export function WithdrawalNotice({
  fulfillmentType,
}: {
  readonly fulfillmentType: 'MadeToOrder' | 'InStock';
}) {
  const titleKey =
    fulfillmentType === 'InStock'
      ? 'checkout.withdrawalNotice.inStock.title'
      : 'checkout.withdrawalNotice.madeToOrder.title';
  const bodyKey =
    fulfillmentType === 'InStock'
      ? 'checkout.withdrawalNotice.inStock.body'
      : 'checkout.withdrawalNotice.madeToOrder.body';

  return (
    <Alert variant="warning">
      <p className="text-xs font-semibold uppercase tracking-wide">
        {t('checkout.withdrawalNotice.interimLabel')}
      </p>
      <p className="mt-1 font-semibold">{t(titleKey)}</p>
      <p className="mt-1">{t(bodyKey)}</p>
    </Alert>
  );
}

/**
 * Collapse `ApiError.fields` (PascalCase property names from
 * FluentValidation) into camelCase-keyed single messages matching the
 * form's state keys (patterns.md B.17).
 */
function normalizeFieldErrors(
  fields: Readonly<Record<string, readonly string[]>>,
): Record<string, string> {
  const normalized: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fields)) {
    if (messages.length === 0) continue;
    const camel = key.charAt(0).toLowerCase() + key.slice(1);
    normalized[camel] = messages[0];
  }
  return normalized;
}

function buildFormError(error: ApiError): string {
  const message = resolveErrorMessage(error);
  // The email-confirmation gate gets a resend hint per T-0084a §C.
  if (error.code === 'auth.emailNotConfirmed') {
    return `${message} ${t('checkout.emailNotConfirmedHint')}`;
  }
  return message;
}

function buildLoginUrl(productId: string): string {
  const target = `/objednavka?productId=${encodeURIComponent(productId)}`;
  // The login page serves at /login — the (auth) route group adds no
  // URL segment.
  return `/login?redirect=${encodeURIComponent(target)}`;
}
