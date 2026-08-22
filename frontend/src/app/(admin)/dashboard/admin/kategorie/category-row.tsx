'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  type AdminCategoryItem,
  deactivateCategory,
  updateCategory,
} from '@/lib/api-client-helpers/admin-categories';
import { t } from '@/lib/i18n';
import { useArmConfirm } from '../_components/use-arm-confirm';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * One category row with inline rename/deactivate (T-0119 /
 * US-admin-0013 AC-2 + AC-3). Rename never touches the slug (public
 * URLs + product FKs survive); deactivate is a soft delete behind a
 * two-step confirm (first click arms, second confirms — the admin
 * modal budget stays reserved for the money/GDPR surfaces).
 */
export function CategoryRow({ item }: { readonly item: AdminCategoryItem }) {
  const router = useRouter();
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(item.name);
  const [description, setDescription] = useState(item.description ?? '');
  const [sortOrder, setSortOrder] = useState(String(item.sortOrder));
  // T-0176 (ADM-L3): auto-disarm, same as the maker actions.
  const { armed: armDeactivate, arm: armDeactivateNow, disarm: disarmDeactivate } = useArmConfirm();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    const parsedSortOrder = Number.parseInt(sortOrder, 10);
    const result = await updateCategory(item.id, {
      name: name.trim(),
      description: description.trim() || undefined,
      sortOrder: Number.isFinite(parsedSortOrder) ? parsedSortOrder : item.sortOrder,
    });

    if (result.success) {
      setEditing(false);
      router.refresh();
    } else {
      setError(resolveErrorMessage(result.error));
    }
    setBusy(false);
  }

  async function handleDeactivate() {
    if (!armDeactivate) {
      armDeactivateNow();
      return;
    }
    setBusy(true);
    setError(null);
    const result = await deactivateCategory(item.id);
    if (result.success) {
      disarmDeactivate();
      router.refresh();
      // T-0176 (audit ADM-H6): the success path never reset `busy`, and
      // router.refresh() does NOT remount this client row — so a
      // successfully deactivated category left its own Edit button
      // (disabled={busy}) permanently dead until a hard reload.
      setBusy(false);
      return;
    }
    setError(resolveErrorMessage(result.error));
    setBusy(false);
  }

  return (
    <div className={`flex flex-col gap-3 p-4 ${item.isActive ? '' : 'opacity-60'}`}>
      {error ? <Alert variant="error">{error}</Alert> : null}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <p className="truncate text-base font-semibold text-white">{item.name}</p>
            <Badge variant={item.isActive ? 'success' : 'default'}>
              {item.isActive
                ? t('dashboard.admin.categories.badge.active')
                : t('dashboard.admin.categories.badge.inactive')}
            </Badge>
          </div>
          <p className="mt-1 text-xs text-zinc-500">
            /{item.slug} · {t('dashboard.admin.categories.row.sortOrder', { order: item.sortOrder })}
          </p>
          {item.description ? (
            <p className="mt-1 text-sm text-zinc-400">{item.description}</p>
          ) : null}
        </div>

        <div className="flex shrink-0 items-center gap-2">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={busy}
            onClick={() => {
              setEditing((v) => !v);
              disarmDeactivate();
            }}
          >
            {!editing ? <Icon name="edit" size={14} /> : null}
            {editing
              ? t('dashboard.admin.categories.row.cancel')
              : t('dashboard.admin.categories.row.edit')}
          </Button>
          {item.isActive ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              loading={busy && armDeactivate}
              onClick={handleDeactivate}
            >
              <Icon name="trash" size={14} />
              {armDeactivate
                ? t('dashboard.admin.categories.row.deactivate_confirm')
                : t('dashboard.admin.categories.row.deactivate')}
            </Button>
          ) : null}
        </div>
      </div>

      {editing ? (
        <form onSubmit={handleSave} className="flex flex-col gap-3 border-t border-zinc-800 pt-3" noValidate>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Input
              label={t('dashboard.admin.categories.form.name')}
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              disabled={busy}
            />
            <Input
              label={t('dashboard.admin.categories.form.sortOrder')}
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(e.target.value)}
              disabled={busy}
            />
          </div>
          <Textarea
            label={t('dashboard.admin.categories.form.description')}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            disabled={busy}
          />
          <p className="text-xs text-zinc-500">{t('dashboard.admin.categories.row.slug_note')}</p>
          <div>
            <Button type="submit" size="sm" loading={busy} disabled={!name.trim() || busy}>
              {!busy ? <Icon name="save" size={14} /> : null}
              {t('dashboard.admin.categories.row.save')}
            </Button>
          </div>
        </form>
      ) : null}
    </div>
  );
}
