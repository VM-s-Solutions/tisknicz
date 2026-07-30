'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  deactivateMaker,
  refreshMakerFromAres,
  verifyMaker,
} from '@/lib/api-client-helpers/admin-makers';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Admin judgment-call actions for one maker (T-0119b wires the T-0034
 * commands over HTTP): verify (shown until verified), refresh-ARES, and
 * deactivate behind a two-step confirm (the modal budget stays reserved
 * for the money/GDPR surfaces — T-0118c lock). All POST →
 * `router.refresh()`, no optimistic UI (Q5 precedent).
 */
export function MakerAdminActions({
  makerId,
  isVerified,
  isActive,
}: {
  readonly makerId: string;
  readonly isVerified: boolean;
  readonly isActive: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<null | 'verify' | 'refresh' | 'deactivate'>(null);
  const [armDeactivate, setArmDeactivate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function run(
    kind: 'verify' | 'refresh' | 'deactivate',
    action: () => Promise<{ success: boolean; error?: { code: string; type: string } }>,
    successMessage: string,
  ) {
    setBusy(kind);
    setError(null);
    setNotice(null);
    const result = await action();
    if (result.success) {
      setNotice(successMessage);
      setArmDeactivate(false);
      router.refresh();
    } else if (result.error) {
      setError(resolveErrorMessage(result.error as Parameters<typeof resolveErrorMessage>[0]));
    }
    setBusy(null);
  }

  return (
    <Card className="flex flex-col gap-4">
      <h2 className="flex items-center gap-3 text-lg font-semibold text-white">
        <span className="icon-tile h-9 w-9 shrink-0" aria-hidden="true">
          <Icon name="settings" size={16} />
        </span>
        {t('dashboard.admin.ops.makers.actions.title')}
      </h2>
      {error ? <Alert variant="error">{error}</Alert> : null}
      {notice ? <Alert variant="success">{notice}</Alert> : null}

      <div className="flex flex-wrap gap-2">
        {!isVerified ? (
          <Button
            type="button"
            loading={busy === 'verify'}
            disabled={busy !== null}
            onClick={() =>
              run('verify', () => verifyMaker(makerId), t('dashboard.admin.ops.makers.actions.verifySuccess'))
            }
          >
            {busy !== 'verify' ? <Icon name="verified" size={16} /> : null}
            {t('dashboard.admin.ops.makers.actions.verify')}
          </Button>
        ) : null}

        <Button
          type="button"
          variant="secondary"
          loading={busy === 'refresh'}
          disabled={busy !== null}
          onClick={() =>
            run(
              'refresh',
              () => refreshMakerFromAres(makerId),
              t('dashboard.admin.ops.makers.actions.refreshSuccess'),
            )
          }
        >
          {busy !== 'refresh' ? <Icon name="refresh" size={16} /> : null}
          {t('dashboard.admin.ops.makers.actions.refresh')}
        </Button>

        {isActive ? (
          <Button
            type="button"
            variant="ghost"
            loading={busy === 'deactivate'}
            disabled={busy !== null}
            onClick={() => {
              if (!armDeactivate) {
                setArmDeactivate(true);
                return;
              }
              void run(
                'deactivate',
                () => deactivateMaker(makerId),
                t('dashboard.admin.ops.makers.actions.deactivateSuccess'),
              );
            }}
          >
            {busy !== 'deactivate' ? <Icon name="xCircle" size={16} /> : null}
            {armDeactivate
              ? t('dashboard.admin.ops.makers.actions.deactivateConfirm')
              : t('dashboard.admin.ops.makers.actions.deactivate')}
          </Button>
        ) : null}
      </div>

      <p className="text-xs text-zinc-500">{t('dashboard.admin.ops.makers.actions.note')}</p>
    </Card>
  );
}
