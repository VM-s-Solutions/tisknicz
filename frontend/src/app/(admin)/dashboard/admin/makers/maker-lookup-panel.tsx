'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

/**
 * Maker-id lookup (T-0140, US-admin-0018). Client island — just navigates
 * to the id-scoped detail route; the actual read/write happens there. See
 * `page.tsx` for why this is a manual lookup rather than a browsable list.
 */
export function MakerLookupPanel() {
  const router = useRouter();
  const [makerId, setMakerId] = useState('');

  const trimmed = makerId.trim();

  function handleSubmit() {
    if (trimmed === '') return;
    router.push(`/dashboard/admin/makers/${encodeURIComponent(trimmed)}`);
  }

  return (
    <Card className="flex flex-col gap-4">
      <Input
        label={t('dashboard.admin.ops.makers.lookup.idLabel')}
        value={makerId}
        onChange={(e) => setMakerId(e.target.value)}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.admin.ops.makers.lookup.idHint')}</p>

      <div className="flex justify-end">
        <Button type="button" disabled={trimmed === ''} onClick={handleSubmit}>
          {t('dashboard.admin.ops.makers.lookup.submit')}
        </Button>
      </div>
    </Card>
  );
}
