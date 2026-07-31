import { Icon, type IconName } from '@/components/ui/icon';

interface SectionHeadingProps {
  readonly icon: IconName;
  /** Section title (already translated). */
  readonly title: string;
  /** Optional supporting line under the title (already translated). */
  readonly hint?: string;
}

/**
 * Icon-tile + title row that opens a card section. Shared by the
 * profile surfaces so the customer and maker forms head their sections
 * the same way rather than each inventing a heading treatment.
 */
export function SectionHeading({ icon, title, hint }: SectionHeadingProps) {
  return (
    <div className="flex items-center gap-3">
      <span className="icon-tile h-9 w-9" aria-hidden="true">
        <Icon name={icon} size={16} />
      </span>
      <div className="min-w-0">
        <h2 className="text-lg font-semibold text-white">{title}</h2>
        {hint && <p className="mt-0.5 text-xs text-zinc-500">{hint}</p>}
      </div>
    </div>
  );
}
