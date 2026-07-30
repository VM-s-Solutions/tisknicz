import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PickupNoteProps {
  readonly enabled: boolean;
  readonly note: string | null | undefined;
}

/**
 * Pickup-note section (T-0047 AC-2). Renders only when the maker has
 * personal pickup enabled AND a non-empty note. Plain text — JSX escapes
 * by default; we intentionally do <b>not</b> use
 * <c>dangerouslySetInnerHTML</c> (sanitization rules belong on the
 * backend, not the renderer).
 */
export function PickupNote({ enabled, note }: PickupNoteProps) {
  if (!enabled) return null;
  const trimmed = note?.trim();
  if (!trimmed) return null;
  return (
    <Card variant="elevated" padding="md" className="flex flex-col gap-3">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="icon-tile h-9 w-9">
          <Icon name="mapPin" size={16} />
        </span>
        <h2 className="text-lg font-semibold text-white">
          {t('catalog.maker.pickup.heading')}
        </h2>
      </div>
      <p className="whitespace-pre-line text-sm text-zinc-300">{trimmed}</p>
    </Card>
  );
}
