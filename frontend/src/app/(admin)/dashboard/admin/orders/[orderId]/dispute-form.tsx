'use client';

import { useRouter } from 'next/navigation';
import { useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import {
  DisputeCategory,
  DisputeResolutionOutcome,
  OrderState,
  openAdminDispute,
  resolveDispute,
} from '@/lib/api-client-helpers/admin-orders';
import { type MessageKey, t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Inline dispute surface for the order detail (T-0118b §A.3 — NOT a
 * modal). Resolve is a reviewed triage decision made after reading the
 * audit trail in the same view, so it renders inline beneath the
 * evidence. Mutually exclusive by state: the RESOLVE form shows when the
 * order is `Disputed`, the OPEN form otherwise (one capability = one
 * surface). Both POST → `router.refresh()` (no optimistic UI) and surface
 * the backend dispute codes via `resolveErrorMessage`. The parenthesis-
 * state semantics are backend business logic — the UI only posts inputs.
 */

const CATEGORY_LABEL_KEYS: Record<DisputeCategory, MessageKey> = {
  [DisputeCategory.NotDelivered]: 'dashboard.admin.orderActions.dispute.category.notDelivered',
  [DisputeCategory.DamagedItem]: 'dashboard.admin.orderActions.dispute.category.damagedItem',
  [DisputeCategory.NotAsDescribed]: 'dashboard.admin.orderActions.dispute.category.notAsDescribed',
  [DisputeCategory.CarrierReturned]: 'dashboard.admin.orderActions.dispute.category.carrierReturned',
  [DisputeCategory.CarrierFailed]: 'dashboard.admin.orderActions.dispute.category.carrierFailed',
  [DisputeCategory.Other]: 'dashboard.admin.orderActions.dispute.category.other',
};

const OUTCOME_LABEL_KEYS: Record<DisputeResolutionOutcome, MessageKey> = {
  [DisputeResolutionOutcome.Refunded]: 'dashboard.admin.orderActions.dispute.outcome.refunded',
  [DisputeResolutionOutcome.Resumed]: 'dashboard.admin.orderActions.dispute.outcome.resumed',
  [DisputeResolutionOutcome.Cancelled]: 'dashboard.admin.orderActions.dispute.outcome.cancelled',
};

const CATEGORY_VALUES: readonly DisputeCategory[] = [
  DisputeCategory.NotDelivered,
  DisputeCategory.DamagedItem,
  DisputeCategory.NotAsDescribed,
  DisputeCategory.CarrierReturned,
  DisputeCategory.CarrierFailed,
  DisputeCategory.Other,
];

const OUTCOME_VALUES: readonly DisputeResolutionOutcome[] = [
  DisputeResolutionOutcome.Refunded,
  DisputeResolutionOutcome.Resumed,
  DisputeResolutionOutcome.Cancelled,
];

export function DisputeForm({
  orderId,
  state,
}: {
  readonly orderId: string;
  readonly state: OrderState;
}) {
  return state === OrderState.Disputed ? (
    <ResolveDisputeForm orderId={orderId} />
  ) : (
    <OpenDisputeForm orderId={orderId} />
  );
}

function OpenDisputeForm({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const [category, setCategory] = useState<string>(DisputeCategory.NotDelivered);
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState(false);

  const descriptionValid = description.trim() !== '';
  const busy = submitting || isRefreshing;
  const canSubmit = descriptionValid && category !== '' && !busy;

  async function handleSubmit() {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);

    const result = await openAdminDispute(orderId, {
      category: category as DisputeCategory,
      description: description.trim(),
    });

    if (result.success) {
      startTransition(() => {
        router.refresh();
      });
      return;
    }

    setError(resolveErrorMessage(result.error));
    setSubmitting(false);
  }

  return (
    <Card padding="md" className="flex flex-col gap-4">
      <div>
        <h3 className="text-sm font-semibold text-zinc-200">
          {t('dashboard.admin.orderActions.dispute.open.title')}
        </h3>
        <p className="mt-1 text-sm text-zinc-400">
          {t('dashboard.admin.orderActions.dispute.open.intro')}
        </p>
      </div>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <Select
        label={t('dashboard.admin.orderActions.dispute.categoryLabel')}
        value={category}
        onChange={(event) => setCategory(event.target.value)}
        disabled={busy}
        options={CATEGORY_VALUES.map((c) => ({ value: c, label: t(CATEGORY_LABEL_KEYS[c]) }))}
      />

      <Textarea
        rows={3}
        label={t('dashboard.admin.orderActions.dispute.descriptionLabel')}
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        disabled={busy}
      />

      <div className="flex items-center justify-end">
        <Button type="button" loading={busy} disabled={!canSubmit} onClick={() => void handleSubmit()}>
          {busy
            ? t('dashboard.admin.orderActions.dispute.open.submitting')
            : t('dashboard.admin.orderActions.dispute.open.submit')}
        </Button>
      </div>
    </Card>
  );
}

function ResolveDisputeForm({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const [outcome, setOutcome] = useState<string>(DisputeResolutionOutcome.Refunded);
  const [resolutionNotes, setResolutionNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState(false);

  const notesValid = resolutionNotes.trim() !== '';
  const busy = submitting || isRefreshing;
  const canSubmit = notesValid && outcome !== '' && !busy;

  async function handleSubmit() {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);

    const result = await resolveDispute(orderId, {
      outcome: outcome as DisputeResolutionOutcome,
      resolutionNotes: resolutionNotes.trim(),
    });

    if (result.success) {
      startTransition(() => {
        router.refresh();
      });
      return;
    }

    setError(resolveErrorMessage(result.error));
    setSubmitting(false);
  }

  return (
    <Card padding="md" className="flex flex-col gap-4">
      <div>
        <h3 className="text-sm font-semibold text-zinc-200">
          {t('dashboard.admin.orderActions.dispute.resolve.title')}
        </h3>
        <p className="mt-1 text-sm text-zinc-400">
          {t('dashboard.admin.orderActions.dispute.resolve.intro')}
        </p>
      </div>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <Select
        label={t('dashboard.admin.orderActions.dispute.outcomeLabel')}
        value={outcome}
        onChange={(event) => setOutcome(event.target.value)}
        disabled={busy}
        options={OUTCOME_VALUES.map((o) => ({ value: o, label: t(OUTCOME_LABEL_KEYS[o]) }))}
      />

      <Textarea
        rows={3}
        label={t('dashboard.admin.orderActions.dispute.resolutionNotesLabel')}
        value={resolutionNotes}
        onChange={(event) => setResolutionNotes(event.target.value)}
        disabled={busy}
      />
      <p className="-mt-2 text-xs text-amber-300">
        {t('dashboard.admin.orderActions.dispute.resolutionNotesHint')}
      </p>

      <div className="flex items-center justify-end">
        <Button type="button" loading={busy} disabled={!canSubmit} onClick={() => void handleSubmit()}>
          {busy
            ? t('dashboard.admin.orderActions.dispute.resolve.submitting')
            : t('dashboard.admin.orderActions.dispute.resolve.submit')}
        </Button>
      </div>
    </Card>
  );
}
