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
 * Shared empty / no-match surface: flat dashed panel with a teal icon
 * tile. Strings arrive pre-translated; the CTA stays a caller-owned
 * slot so each page keeps its own link target and copy.
 */
export function EmptyState({ icon, title, description, action }: EmptyStateProps) {
  return (
    <div className="rounded-xl border border-dashed border-zinc-700 bg-surface-card px-6 py-16 text-center sm:py-20">
      <div className="flex flex-col items-center gap-5">
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
