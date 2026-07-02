interface MakablesLogoProps {
  className?: string;
  iconClassName?: string;
  textClassName?: string;
}

export function MakablesLogo({
  className = '',
  iconClassName = 'h-7 w-7',
  textClassName = 'text-lg font-semibold tracking-tight text-zinc-100',
}: MakablesLogoProps) {
  return (
    <span className={`inline-flex items-center gap-2 ${className}`.trim()}>
      <svg
        viewBox="0 0 24 24"
        aria-hidden="true"
        className={`${iconClassName} text-brand-400`.trim()}
        fill="none"
      >
        <path d="M8.2 12c0-3.55 1.7-6 3.8-6s3.8 2.45 3.8 6-1.7 6-3.8 6-3.8-2.45-3.8-6z" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
        <path d="M12 8.2c3.55 0 6 1.7 6 3.8s-2.45 3.8-6 3.8-6-1.7-6-3.8 2.45-3.8 6-3.8z" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
        <circle cx="12" cy="12" r="1.05" fill="currentColor" />
      </svg>
      <span className={textClassName}>Makables</span>
    </span>
  );
}