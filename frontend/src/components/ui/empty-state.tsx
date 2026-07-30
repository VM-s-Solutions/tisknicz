import type { ReactNode } from 'react';
import { Icon, type IconName } from '@/components/ui/icon';

interface EmptyStateProps {
  readonly icon: IconName;
  readonly title: string;
  readonly description?: string;
  /** Slot for a CTA link/button, rendered under the copy. */
  readonly action?: ReactNode;
}

/**
 * Shared empty / no-match surface: dashed panel with a glowing icon
 * tile. Strings arrive pre-translated; the CTA stays a caller-owned
 * slot so each page keeps its own link target and copy.
 */
export function EmptyState({ icon, title, description, action }: EmptyStateProps) {
  return (
    <div className="relative overflow-hidden rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div
        aria-hidden="true"
        className="empty-glow pointer-events-none absolute inset-x-0 top-0 h-40"
      />
      <div className="relative flex flex-col items-center gap-5">
        <div className="icon-tile h-16 w-16">
          <Icon name={icon} size={28} />
        </div>
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">{title}</h2>
          {description && (
            <p className="mx-auto mt-2 max-w-md text-sm leading-relaxed text-zinc-400">
              {description}
            </p>
          )}
        </div>
        {action && <div className="mt-1">{action}</div>}
      </div>
    </div>
  );
}
