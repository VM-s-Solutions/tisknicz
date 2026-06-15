'use client';

import { useEffect, useId, useRef, useState, useTransition } from 'react';
import { useRouter } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import {
  InvoicingMode,
  type UpdateCountryConfigInput,
  updateCountryConfig,
} from '@/lib/api-client-helpers/admin-ops-client';
import { type MessageKey, t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Country-config edit form (T-0118c §2). Client island. Edits the T-0108
 * editable set + a mandatory reason. On submit, if ANY `Default*Provider`
 * field changed, the save opens a confirmation modal that requires
 * retyping the NEW provider code (A.5 — the retype-the-code idiom, distinct
 * from the delete-user retype-the-email and lower blast radius). A
 * VAT/fee-only edit saves directly with no modal. The provider gate, the
 * unregistered-code rejection (`country.providerNotRegistered`), the
 * mismatch (`country.providerConfirmationMismatch`) and the
 * `inFlightOrderCount` advisory are ALL backend — this form posts inputs
 * and renders the verdict. No business logic, no pricing math: the bp
 * fields are entered/sent verbatim, the shipping price scales whole-CZK →
 * minor units at submit (display scaling only). Disabled-while-pending.
 *
 * NO server pre-fill (no GET on the contract — gap): every field starts
 * blank and the operator enters the full set.
 */

const INVOICING_MODE_VALUES: readonly InvoicingMode[] = [
  InvoicingMode.None,
  InvoicingMode.StandardVat,
  InvoicingMode.ReverseCharge,
  InvoicingMode.StrictFiscalReporting,
];

const INVOICING_MODE_LABEL_KEYS: Record<InvoicingMode, MessageKey> = {
  [InvoicingMode.None]: 'dashboard.admin.ops.country.invoicingMode.none',
  [InvoicingMode.StandardVat]: 'dashboard.admin.ops.country.invoicingMode.standardVat',
  [InvoicingMode.ReverseCharge]: 'dashboard.admin.ops.country.invoicingMode.reverseCharge',
  [InvoicingMode.StrictFiscalReporting]:
    'dashboard.admin.ops.country.invoicingMode.strictFiscalReporting',
};

/** Parse a whole-number field; empty / non-numeric → null (the field stays invalid for required ones). */
function parseInt0(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === '') return null;
  const parsed = Number.parseInt(trimmed, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

interface FormState {
  readonly standardVatBp: string;
  readonly reducedVatBp: string;
  readonly platformFeeBp: string;
  readonly shippingPriceWhole: string;
  readonly invoicingMode: InvoicingMode;
  readonly paymentProvider: string;
  readonly shippingCarrier: string;
  readonly registry: string;
  readonly emailProvider: string;
  readonly reason: string;
}

const INITIAL_FORM: FormState = {
  standardVatBp: '',
  reducedVatBp: '',
  platformFeeBp: '',
  shippingPriceWhole: '',
  invoicingMode: InvoicingMode.StandardVat,
  paymentProvider: '',
  shippingCarrier: '',
  registry: '',
  emailProvider: '',
  reason: '',
};

export function CountryConfigForm({ countryCode }: { readonly countryCode: string }) {
  const router = useRouter();
  const [form, setForm] = useState<FormState>(INITIAL_FORM);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [advisory, setAdvisory] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [isRefreshing, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  const standardVatBp = parseInt0(form.standardVatBp);
  const platformFeeBp = parseInt0(form.platformFeeBp);
  const shippingWhole = parseInt0(form.shippingPriceWhole);
  const reducedVatBp = form.reducedVatBp.trim() === '' ? undefined : parseInt0(form.reducedVatBp);
  const reasonValid = form.reason.trim() !== '';

  // A provider field is "set" when non-empty; ALL four are required for a
  // valid PUT (the backend validates registration). Any non-empty provider
  // value means the save is treated as a provider change → modal (the form
  // has no prior value to diff against because there is no GET; treating a
  // provided code as a change is the safe, friction-preserving default per
  // A.5 — the operator confirms the code they intend to set).
  const providers = {
    paymentProvider: form.paymentProvider.trim(),
    shippingCarrier: form.shippingCarrier.trim(),
    registry: form.registry.trim(),
    emailProvider: form.emailProvider.trim(),
  };
  const anyProviderSet = Object.values(providers).some((v) => v !== '');

  const baseValid =
    standardVatBp !== null &&
    platformFeeBp !== null &&
    shippingWhole !== null &&
    providers.paymentProvider !== '' &&
    providers.shippingCarrier !== '' &&
    providers.registry !== '' &&
    providers.emailProvider !== '' &&
    reasonValid;

  const busy = submitting || isRefreshing;

  function buildPayload(confirmedProviderCode: string | undefined): UpdateCountryConfigInput {
    return {
      standardVatRateBp: standardVatBp ?? 0,
      reducedVatRateBp: reducedVatBp ?? undefined,
      invoicingMode: form.invoicingMode,
      platformFeeRateBp: platformFeeBp ?? 0,
      defaultShippingPriceMinor: (shippingWhole ?? 0) * 100,
      defaultPaymentProvider: providers.paymentProvider,
      defaultShippingCarrier: providers.shippingCarrier,
      defaultRegistry: providers.registry,
      defaultEmailProvider: providers.emailProvider,
      confirmedProviderCode,
      reason: form.reason.trim(),
    };
  }

  async function submit(confirmedProviderCode: string | undefined): Promise<boolean> {
    if (inFlightRef.current) return false;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);
    setSuccess(null);
    setAdvisory(null);

    const result = await updateCountryConfig(countryCode, buildPayload(confirmedProviderCode));

    if (result.success) {
      setSuccess(t('dashboard.admin.ops.country.success'));
      if (result.value.providerChanged && result.value.inFlightOrderCount > 0) {
        setAdvisory(
          t('dashboard.admin.ops.country.inFlightAdvisory', {
            count: result.value.inFlightOrderCount,
          }),
        );
      }
      startTransition(() => {
        router.refresh();
      });
      inFlightRef.current = false;
      setSubmitting(false);
      return true;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
    return false;
  }

  function handlePrimaryClick() {
    if (!baseValid || busy) return;
    // Provider set → open the retype-the-code modal (A.5). VAT/fee-only
    // edits would skip it; here any provided code is confirmed via the
    // modal (no prior value to diff against — no GET).
    if (anyProviderSet) {
      setModalOpen(true);
      return;
    }
    void submit(undefined);
  }

  return (
    <Card className="flex flex-col gap-5">
      {error ? <Alert variant="error">{error}</Alert> : null}
      {success ? <Alert variant="success">{success}</Alert> : null}
      {advisory ? <Alert variant="warning">{advisory}</Alert> : null}

      <fieldset className="flex flex-col gap-4">
        <legend className="mb-2 text-sm font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.ops.country.section.tax')}
        </legend>
        <div>
          <Input
            type="number"
            inputMode="numeric"
            min={0}
            label={t('dashboard.admin.ops.country.standardVatLabel')}
            value={form.standardVatBp}
            onChange={(e) => setForm((f) => ({ ...f, standardVatBp: e.target.value }))}
            disabled={busy}
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.country.standardVatHint')}
          </p>
        </div>
        <div>
          <Input
            type="number"
            inputMode="numeric"
            min={0}
            label={t('dashboard.admin.ops.country.reducedVatLabel')}
            value={form.reducedVatBp}
            onChange={(e) => setForm((f) => ({ ...f, reducedVatBp: e.target.value }))}
            disabled={busy}
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.country.reducedVatHint')}
          </p>
        </div>
        <div>
          <Input
            type="number"
            inputMode="numeric"
            min={0}
            label={t('dashboard.admin.ops.country.platformFeeLabel')}
            value={form.platformFeeBp}
            onChange={(e) => setForm((f) => ({ ...f, platformFeeBp: e.target.value }))}
            disabled={busy}
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.country.platformFeeHint')}
          </p>
        </div>
        <div>
          <Input
            type="number"
            inputMode="numeric"
            min={0}
            label={t('dashboard.admin.ops.country.shippingPriceLabel')}
            value={form.shippingPriceWhole}
            onChange={(e) => setForm((f) => ({ ...f, shippingPriceWhole: e.target.value }))}
            disabled={busy}
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.country.shippingPriceHint')}
          </p>
        </div>
        <Select
          label={t('dashboard.admin.ops.country.invoicingModeLabel')}
          value={form.invoicingMode}
          onChange={(e) => setForm((f) => ({ ...f, invoicingMode: e.target.value as InvoicingMode }))}
          disabled={busy}
          options={INVOICING_MODE_VALUES.map((m) => ({
            value: m,
            label: t(INVOICING_MODE_LABEL_KEYS[m]),
          }))}
        />
      </fieldset>

      <fieldset className="flex flex-col gap-4 border-t border-zinc-800 pt-5">
        <legend className="mb-2 text-sm font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.ops.country.section.providers')}
        </legend>
        <Input
          label={t('dashboard.admin.ops.country.paymentProviderLabel')}
          value={form.paymentProvider}
          onChange={(e) => setForm((f) => ({ ...f, paymentProvider: e.target.value }))}
          disabled={busy}
          autoComplete="off"
          spellCheck={false}
        />
        <Input
          label={t('dashboard.admin.ops.country.shippingCarrierLabel')}
          value={form.shippingCarrier}
          onChange={(e) => setForm((f) => ({ ...f, shippingCarrier: e.target.value }))}
          disabled={busy}
          autoComplete="off"
          spellCheck={false}
        />
        <Input
          label={t('dashboard.admin.ops.country.registryLabel')}
          value={form.registry}
          onChange={(e) => setForm((f) => ({ ...f, registry: e.target.value }))}
          disabled={busy}
          autoComplete="off"
          spellCheck={false}
        />
        <Input
          label={t('dashboard.admin.ops.country.emailProviderLabel')}
          value={form.emailProvider}
          onChange={(e) => setForm((f) => ({ ...f, emailProvider: e.target.value }))}
          disabled={busy}
          autoComplete="off"
          spellCheck={false}
        />
      </fieldset>

      <div className="border-t border-zinc-800 pt-5">
        <Textarea
          rows={3}
          label={t('dashboard.admin.ops.country.reasonLabel')}
          value={form.reason}
          onChange={(e) => setForm((f) => ({ ...f, reason: e.target.value }))}
          disabled={busy}
        />
        <p className="mt-1 text-xs text-zinc-500">{t('dashboard.admin.ops.country.reasonHint')}</p>
      </div>

      <div className="flex justify-end">
        <Button
          type="button"
          loading={busy}
          disabled={!baseValid || busy}
          onClick={handlePrimaryClick}
        >
          {busy
            ? t('dashboard.admin.ops.country.saving')
            : t('dashboard.admin.ops.country.save')}
        </Button>
      </div>

      {modalOpen ? (
        <ProviderConfirmModal
          providers={providers}
          busy={busy}
          onClose={() => {
            if (!busy) setModalOpen(false);
          }}
          onConfirm={async (code) => {
            const okResult = await submit(code);
            if (okResult) setModalOpen(false);
          }}
        />
      ) : null}
    </Card>
  );
}

function ProviderConfirmModal({
  providers,
  busy,
  onClose,
  onConfirm,
}: {
  readonly providers: Readonly<Record<string, string>>;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onConfirm: (confirmedProviderCode: string) => void | Promise<void>;
}) {
  const titleId = useId();
  const [typed, setTyped] = useState('');

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !busy) onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [onClose, busy]);

  const typedValid = typed.trim() !== '';

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4"
    >
      <div
        aria-hidden="true"
        onClick={() => {
          if (!busy) onClose();
        }}
        className="absolute inset-0 bg-black/70"
      />
      <div className="relative z-10 my-8 w-full max-w-lg rounded-2xl border border-zinc-800 bg-surface-card p-6 shadow-2xl">
        <h2 id={titleId} className="text-lg font-semibold text-white">
          {t('dashboard.admin.ops.country.providerModal.title')}
        </h2>
        <div className="mt-4 flex flex-col gap-4">
          <p className="text-sm text-zinc-400">
            {t('dashboard.admin.ops.country.providerModal.intro')}
          </p>
          <div className="rounded-xl border border-zinc-800 bg-zinc-900/60 p-3">
            <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500">
              {t('dashboard.admin.ops.country.providerModal.changedHeading')}
            </p>
            <ul className="mt-2 flex flex-col gap-1 text-sm text-zinc-200">
              {Object.entries(providers)
                .filter(([, value]) => value !== '')
                .map(([key, value]) => (
                  <li key={key} className="font-mono">
                    {value}
                  </li>
                ))}
            </ul>
          </div>
          <Input
            label={t('dashboard.admin.ops.country.providerModal.confirmLabel')}
            placeholder={t('dashboard.admin.ops.country.providerModal.confirmPlaceholder')}
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            disabled={busy}
            autoComplete="off"
            spellCheck={false}
          />
          <div className="mt-2 flex items-center justify-end gap-3">
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t('common.cancel')}
            </Button>
            <Button
              type="button"
              loading={busy}
              disabled={!typedValid || busy}
              onClick={() => void onConfirm(typed.trim())}
            >
              {busy
                ? t('dashboard.admin.ops.country.providerModal.confirming')
                : t('dashboard.admin.ops.country.providerModal.confirm')}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
