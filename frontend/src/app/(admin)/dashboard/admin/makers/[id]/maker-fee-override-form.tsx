'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { setMakerFeeOverride } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Maker fee-rate override form (T-0140, US-admin-0018). Client island —
 * follows the `VerifyMaker`/`DeactivateMaker` admin-judgment-call precedent
 * (T-0034) and the country-config edit form's mandatory-reason shape.
 *
 * Sets or clears `Maker.FeeRateOverrideBp` via `POST
 * /makers/{id}/fee-override`. Basis points are an implementation detail —
 * the operator enters/reads a percentage ("3,5 %"), and this component
 * converts to/from bp at the submit boundary only (display scaling, NOT
 * business logic; `Math.round(percent * 100)` mirrors the country-config
 * form's whole-CZK → minor-unit scaling).
 *
 * Client-side validation (bp &ge; 0, bp &le; the country default when
 * known) mirrors `SetMakerFeeOverride.Validator` / `Handler` for UX only —
 * defense in depth, never a replacement. The backend re-checks both rules
 * and is authoritative (AC-5); a discount-only ceiling that exceeds the
 * country default is rejected with `maker.feeOverrideExceedsCountryDefault`.
 *
 * `countryDefaultBp` is `undefined` when the country-config read failed
 * (transient error) — the ceiling check is then skipped client-side and
 * left entirely to the backend, which still has the real
 * `CountryConfiguration` row loaded server-side.
 */

type Mode = 'set' | 'clear';

const REASON_MAX_LENGTH = 2000;

/** Percent string ("3,5" or "3.5") → whole-number basis points, or `null` if not a valid non-negative number. */
function parsePercentToBp(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === '') return null;
  const normalized = trimmed.replace(',', '.');
  const percent = Number.parseFloat(normalized);
  if (!Number.isFinite(percent) || percent < 0) return null;
  return Math.round(percent * 100);
}

/** Whole-number basis points → a Czech-formatted percent string ("3,5") for display. */
function bpToPercentDisplay(bp: number): string {
  return (bp / 100).toString().replace('.', ',');
}

export function MakerFeeOverrideForm({
  makerId,
  countryCode,
  countryDefaultBp,
  currentOverrideBp,
}: {
  readonly makerId: string;
  readonly countryCode: string;
  readonly countryDefaultBp: number | undefined;
  /** Existing override, so adjusting 3 % → 2,5 % doesn't mean reading it
   * off the header card and retyping (T-0176, audit ADM-L4). */
  readonly currentOverrideBp?: number | null;
}) {
  const router = useRouter();
  const [mode, setMode] = useState<Mode>('set');
  const [percent, setPercent] = useState(
    currentOverrideBp != null ? bpToPercentDisplay(currentOverrideBp) : '',
  );
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const trimmedReason = reason.trim();
  const reasonValid = trimmedReason !== '' && trimmedReason.length <= REASON_MAX_LENGTH;

  const bp = mode === 'set' ? parsePercentToBp(percent) : null;
  const percentValid = mode === 'clear' || bp !== null;
  const exceedsCeiling =
    mode === 'set' && bp !== null && countryDefaultBp !== undefined && bp > countryDefaultBp;

  const canSubmit = reasonValid && percentValid && !exceedsCeiling && !submitting;

  async function handleSubmit() {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    setSuccess(null);

    const result = await setMakerFeeOverride(makerId, {
      feeRateOverrideBp: mode === 'set' ? bp : null,
      reason: trimmedReason,
    });

    if (result.success) {
      setSuccess(
        mode === 'set'
          ? t('dashboard.admin.ops.makers.feeOverride.successSet', {
              percent: bp !== null ? bpToPercentDisplay(bp) : '',
            })
          : t('dashboard.admin.ops.makers.feeOverride.successClear'),
      );
      setReason('');
      router.refresh();
    } else {
      setError(resolveErrorMessage(result.error));
    }
    setSubmitting(false);
  }

  return (
    <Card className="flex flex-col gap-5">
      {error ? <Alert variant="error">{error}</Alert> : null}
      {success ? <Alert variant="success">{success}</Alert> : null}

      <div className="rounded-xl border border-zinc-800 bg-surface-secondary p-4">
        <p className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.ops.makers.detail.countryDefaultLabel', { country: countryCode })}
        </p>
        <p className="mt-1 text-sm text-zinc-200">
          {countryDefaultBp !== undefined
            ? bpToPercentDisplay(countryDefaultBp) + ' %'
            : t('dashboard.admin.ops.makers.detail.countryDefaultUnavailable')}
        </p>
      </div>

      <Dropdown
        label={t('dashboard.admin.ops.makers.feeOverride.modeLabel')}
        value={mode}
        onChange={(value) => setMode(value as Mode)}
        disabled={submitting}
        options={[
          { value: 'set', label: t('dashboard.admin.ops.makers.feeOverride.modeSet') },
          { value: 'clear', label: t('dashboard.admin.ops.makers.feeOverride.modeClear') },
        ]}
      />

      {mode === 'set' ? (
        <div>
          <Input
            type="text"
            inputMode="decimal"
            label={t('dashboard.admin.ops.makers.feeOverride.percentLabel')}
            value={percent}
            onChange={(e) => setPercent(e.target.value)}
            disabled={submitting}
            placeholder="3,5"
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.makers.feeOverride.percentHint')}
          </p>
          {percent.trim() !== '' && bp === null ? (
            <p className="mt-1 text-xs text-warning">
              {t('dashboard.admin.ops.makers.feeOverride.percentInvalid')}
            </p>
          ) : null}
          {exceedsCeiling ? (
            <p className="mt-1 text-xs text-warning">
              {t('dashboard.admin.ops.makers.feeOverride.exceedsDefault')}
            </p>
          ) : null}
        </div>
      ) : null}

      <div>
        <Textarea
          rows={3}
          label={t('dashboard.admin.ops.makers.feeOverride.reasonLabel')}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={submitting}
          maxLength={REASON_MAX_LENGTH}
        />
        <p className="mt-1 text-xs text-zinc-500">
          {t('dashboard.admin.ops.makers.feeOverride.reasonHint')}
        </p>
      </div>

      <div className="flex justify-end">
        <Button type="button" loading={submitting} disabled={!canSubmit} onClick={() => void handleSubmit()}>
          {!submitting ? <Icon name="save" size={16} /> : null}
          {submitting
            ? t('dashboard.admin.ops.makers.feeOverride.submitting')
            : mode === 'set'
              ? t('dashboard.admin.ops.makers.feeOverride.submitSet')
              : t('dashboard.admin.ops.makers.feeOverride.submitClear')}
        </Button>
      </div>
    </Card>
  );
}
