'use client';

import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { eraseUser } from '@/lib/api-client-helpers/admin-ops-client';
import { userHasInFlightOrders } from '@/lib/api-client-helpers/admin-orders';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * GDPR delete-user panel (T-0118c §4 / T-0127 re-wire) — the most carefully-
 * built component in the slice. THREE phases: LOOKUP (enter the user id +
 * email) → CONFIRM (the erase surface) → TERMINAL (a "deleted" confirmation,
 * NOT a refresh into a 404).
 *
 * The double interlock the backend enforces (T-0110) is mirrored
 * PROACTIVELY on the client — the UI ADDS friction and surfaces the
 * backend's verdict; it NEVER replaces the server gate:
 *   - Irreversibility banner (A.2a) — prominent, ALWAYS visible on the
 *     confirm surface.
 *   - Type-the-exact-email (A.1) — the destructive button is disabled until
 *     the typed value EXACTLY matches the displayed email. This is the
 *     single strongest friction the admin UI offers and is RESERVED for
 *     this screen (no other admin surface type-to-confirms). The backend
 *     re-checks via `user.deleteConfirmationMismatch`.
 *   - In-flight-order block (A.2b) — server-enforced. T-0127 added the
 *     per-user in-flight signal (`userHasInFlightOrders`), so the block is
 *     now surfaced PROACTIVELY: on lookup-submit the panel probes the
 *     filtered admin-orders read; if the user has any order in an in-flight
 *     state the destructive button is disabled PRE-call with the
 *     `user.cannotDeleteWithInFlightOrders` reason inline (T-0118c AC-12).
 *     The backend T-0110 gate stays authoritative and re-checks at submit;
 *     this is the proactive UX layer, replacing the prior post-submit
 *     reactive verdict.
 *   - Mandatory reason (≤ 2000, GDPR ticket ref) — backend authoritative.
 *
 * The button enable predicate (email-match AND non-empty-reason AND
 * not-in-flight) is purely presentational; T-0110 re-checks every gate
 * regardless. Disabled-while-pending on the erase. A re-call after a
 * successful erase (or an already-gone user) surfaces `user.notFound`
 * rendered as "uživatel již byl smazán" — NOT a silent success (T-0110's
 * no-Silent-Success rule).
 */

const REASON_MAX_LENGTH = 2000;

type Phase = 'lookup' | 'confirm' | 'deleted';

/**
 * The proactive in-flight verdict resolved at lookup-submit (T-0127):
 *   - `clear` — no in-flight order; erase is allowed to proceed.
 *   - `blocked` — the user has an in-flight order; the destructive button is
 *     pre-disabled with the inline reason.
 *   - `unknown` — the probe read failed transiently; do NOT pre-disable (the
 *     backend gate re-checks at submit and stays authoritative).
 */
type InFlightVerdict = 'clear' | 'blocked' | 'unknown';

export function DeleteUserPanel() {
  const [phase, setPhase] = useState<Phase>('lookup');
  const [userId, setUserId] = useState('');
  const [userEmail, setUserEmail] = useState('');
  const [inFlightVerdict, setInFlightVerdict] = useState<InFlightVerdict>('unknown');
  const [probing, setProbing] = useState(false);
  const probeRef = useRef(false);

  async function handleLookupSubmit() {
    const id = userId.trim();
    if (probeRef.current || id === '' || userEmail.trim() === '') return;
    probeRef.current = true;
    setProbing(true);

    // Proactive in-flight probe (T-0127 AC-12). A failed read → `unknown`
    // (do NOT pre-disable on a transient error — the backend gate is
    // authoritative and re-checks at submit).
    const result = await userHasInFlightOrders(id);
    setInFlightVerdict(result.success ? (result.value ? 'blocked' : 'clear') : 'unknown');

    probeRef.current = false;
    setProbing(false);
    setPhase('confirm');
  }

  function reset() {
    setPhase('lookup');
    setUserId('');
    setUserEmail('');
    setInFlightVerdict('unknown');
  }

  if (phase === 'deleted') {
    return <DeletedConfirmation userId={userId.trim()} onReset={reset} />;
  }

  if (phase === 'confirm') {
    return (
      <EraseConfirmation
        userId={userId.trim()}
        userEmail={userEmail.trim()}
        inFlightVerdict={inFlightVerdict}
        onDeleted={() => setPhase('deleted')}
        onBack={reset}
      />
    );
  }

  return (
    <Card className="flex flex-col gap-4">
      <div>
        <h2 className="text-sm font-semibold text-zinc-200">
          {t('dashboard.admin.ops.users.lookup.heading')}
        </h2>
        <p className="mt-1 text-sm text-zinc-400">
          {t('dashboard.admin.ops.users.lookup.intro')}
        </p>
      </div>

      <Input
        label={t('dashboard.admin.ops.users.lookup.idLabel')}
        value={userId}
        onChange={(e) => setUserId(e.target.value)}
        disabled={probing}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.admin.ops.users.lookup.idHint')}</p>

      <Input
        type="email"
        label={t('dashboard.admin.ops.users.lookup.emailLabel')}
        value={userEmail}
        onChange={(e) => setUserEmail(e.target.value)}
        disabled={probing}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">
        {t('dashboard.admin.ops.users.lookup.emailHint')}
      </p>

      <div className="flex justify-end">
        <Button
          type="button"
          variant="danger"
          loading={probing}
          disabled={userId.trim() === '' || userEmail.trim() === '' || probing}
          onClick={() => void handleLookupSubmit()}
        >
          {!probing ? <Icon name="search" size={16} /> : null}
          {probing
            ? t('dashboard.admin.ops.users.lookup.checking')
            : t('dashboard.admin.ops.users.lookup.submit')}
        </Button>
      </div>
    </Card>
  );
}

function IrreversibilityBanner() {
  return (
    <Alert variant="error">
      <div>
        <p className="font-semibold">{t('dashboard.admin.ops.users.irreversibleBanner.title')}</p>
        <p className="mt-1 text-sm opacity-90">
          {t('dashboard.admin.ops.users.irreversibleBanner.body')}
        </p>
      </div>
    </Alert>
  );
}

function EraseConfirmation({
  userId,
  userEmail,
  inFlightVerdict,
  onDeleted,
  onBack,
}: {
  readonly userId: string;
  readonly userEmail: string;
  readonly inFlightVerdict: InFlightVerdict;
  readonly onDeleted: () => void;
  readonly onBack: () => void;
}) {
  const [confirmEmail, setConfirmEmail] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  /** Set when the backend rejects with the in-flight-order block — rendered inline as the reason. */
  const [inFlightBlocked, setInFlightBlocked] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  // Proactive pre-disable (T-0127 AC-12): the lookup probe found an in-flight
  // order → the destructive button is disabled PRE-call with the inline
  // reason. The backend T-0110 gate stays authoritative; `unknown` (transient
  // probe failure) does NOT pre-disable.
  const preBlocked = inFlightVerdict === 'blocked';
  const showInFlightReason = preBlocked || inFlightBlocked;

  const trimmedReason = reason.trim();
  // Presentational predicate ONLY — the server re-checks every gate (T-0110).
  const emailMatches = confirmEmail === userEmail;
  const reasonValid = trimmedReason !== '' && trimmedReason.length <= REASON_MAX_LENGTH;
  const canSubmit = emailMatches && reasonValid && !preBlocked && !submitting;

  async function handleErase() {
    if (inFlightRef.current || !canSubmit) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);
    setInFlightBlocked(false);

    const result = await eraseUser(userId, {
      confirmedEmail: confirmEmail,
      reason: trimmedReason,
    });

    if (result.success) {
      // Terminal "deleted" confirmation — NOT a refresh into a 404.
      onDeleted();
      return;
    }

    // The in-flight-order block (A.2b) surfaces as the backend verdict —
    // render the dedicated inline reason. A re-call after a successful
    // erase (or an already-gone user) returns `user.notFound`, which AC-14
    // renders as "uživatel již byl smazán" (already deleted) — NOT a silent
    // success (T-0110's no-Silent-Success rule). Everything else falls back
    // to the generic resolved message.
    if (result.error.code === 'user.cannotDeleteWithInFlightOrders') {
      setInFlightBlocked(true);
      setError(resolveErrorMessage(result.error));
    } else if (result.error.code === 'user.notFound') {
      setError(t('dashboard.admin.ops.users.erase.alreadyDeleted'));
    } else {
      setError(resolveErrorMessage(result.error));
    }
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <Card className="flex flex-col gap-5">
      {/* A.2a — ALWAYS visible, prominent. */}
      <IrreversibilityBanner />

      <div className="rounded-xl border border-zinc-800 bg-surface-secondary p-4">
        <p className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.ops.users.targetEmailLabel')}
        </p>
        <p className="mt-1 break-all font-mono text-sm text-zinc-100">{userEmail}</p>
      </div>

      {/* In-flight block (A.2b) — surfaced PROACTIVELY when the lookup probe
          found an in-flight order (T-0127 AC-12), or reactively if the
          backend rejects an `unknown`-probe submit. */}
      {showInFlightReason ? (
        <Alert variant="warning">
          <p className="text-sm">{t('dashboard.admin.ops.users.inFlightReason')}</p>
        </Alert>
      ) : null}

      {error && !showInFlightReason ? <Alert variant="error">{error}</Alert> : null}

      <div>
        <Input
          type="email"
          label={t('dashboard.admin.ops.users.confirmEmailLabel')}
          placeholder={t('dashboard.admin.ops.users.confirmEmailPlaceholder')}
          value={confirmEmail}
          onChange={(e) => setConfirmEmail(e.target.value)}
          disabled={submitting}
          autoComplete="off"
          spellCheck={false}
        />
        {confirmEmail !== '' && !emailMatches ? (
          <p className="mt-1 text-xs text-amber-300">
            {t('dashboard.admin.ops.users.confirmEmailMismatch')}
          </p>
        ) : null}
      </div>

      <div>
        <Textarea
          rows={3}
          label={t('dashboard.admin.ops.users.reasonLabel')}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={submitting}
          maxLength={REASON_MAX_LENGTH}
        />
        <p className="mt-1 text-xs text-zinc-500">{t('dashboard.admin.ops.users.reasonHint')}</p>
      </div>

      {!canSubmit && !submitting && !preBlocked ? (
        <p className="text-xs text-zinc-500">{t('dashboard.admin.ops.users.erase.disabledHint')}</p>
      ) : null}

      <div className="flex items-center justify-between gap-3">
        <Button type="button" variant="outline" onClick={onBack} disabled={submitting}>
          {t('common.back')}
        </Button>
        <Button
          type="button"
          variant="danger"
          loading={submitting}
          disabled={!canSubmit}
          onClick={() => void handleErase()}
        >
          <Icon name="trash" size={16} />
          {submitting
            ? t('dashboard.admin.ops.users.erase.pending')
            : t('dashboard.admin.ops.users.erase.button')}
        </Button>
      </div>
    </Card>
  );
}

function DeletedConfirmation({
  userId,
  onReset,
}: {
  readonly userId: string;
  readonly onReset: () => void;
}) {
  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-950/50 text-emerald-400">
          <Icon name="checkCircle" size={20} />
        </span>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.admin.ops.users.erase.success.title')}
        </h2>
      </div>
      <p className="text-sm text-zinc-400">
        {t('dashboard.admin.ops.users.erase.success.body', { userId })}
      </p>
      <div className="flex justify-end">
        <Button type="button" variant="outline" onClick={onReset}>
          {t('dashboard.admin.ops.users.reset')}
        </Button>
      </div>
    </Card>
  );
}
