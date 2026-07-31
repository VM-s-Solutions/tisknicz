import type { ReactNode } from 'react';
import { Icon, type IconName } from '@/components/ui/icon';

interface PageHeaderProps {
  /** Small uppercase label above the title (already translated). */
  readonly eyebrow?: string;
  readonly eyebrowIcon?: IconName;
  /** Page title (already translated). */
  readonly title: string;
  readonly subtitle?: string;
  /** Right-aligned slot for a primary action (button / link). */
  readonly actions?: ReactNode;
}

/**
 * Shared page header for dashboard + catalog surfaces: eyebrow chip,
 * shining headline, subtitle and an optional action slot. Server
 * Component — all strings arrive pre-translated from the page.
 */
export function PageHeader({ eyebrow, eyebrowIcon, title, subtitle, actions }: PageHeaderProps) {
  return (
    <header className="flex flex-col gap-6 sm:flex-row sm:items-end sm:justify-between">
      <div className="max-w-3xl">
        {eyebrow && (
          <p className="mb-2 flex items-center gap-2 text-sm font-medium text-brand-400">
            {eyebrowIcon && <Icon name={eyebrowIcon} size={14} />}
            {eyebrow}
          </p>
        )}
        <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">{title}</h1>
        {subtitle && (
          <p className="mt-3 max-w-2xl text-base leading-relaxed text-zinc-400">{subtitle}</p>
        )}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-3">{actions}</div>}
    </header>
  );
}
