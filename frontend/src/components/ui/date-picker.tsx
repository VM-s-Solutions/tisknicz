'use client';

import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface DatePickerProps {
  /**
   * `''` (empty), `yyyy-MM-dd`, or — with `withTime` — `yyyy-MM-ddTHH:mm`.
   * Same wire shape as native `<input type="date">`/`datetime-local`, so
   * existing URL/state plumbing keeps working unchanged.
   */
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label?: string;
  readonly error?: string;
  readonly placeholder?: string;
  /** Adds an HH:mm time row to the popover and the `T` segment to the value. */
  readonly withTime?: boolean;
  readonly disabled?: boolean;
  /** Inclusive bounds, `yyyy-MM-dd`. */
  readonly min?: string;
  readonly max?: string;
  readonly id?: string;
  readonly className?: string;
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

function toIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function parseValue(value: string): { date: Date | null; time: string } {
  if (!value) return { date: null, time: '00:00' };
  const [datePart, timePart] = value.split('T');
  const [year, month, day] = datePart.split('-').map(Number);
  if (!year || !month || !day) return { date: null, time: '00:00' };
  return { date: new Date(year, month - 1, day), time: timePart?.slice(0, 5) ?? '00:00' };
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/**
 * Custom date / datetime picker on the solid dark surface system —
 * native `<input type="date">` popups can't be themed. The trigger is
 * styled like `Input`; the popover is a solid `bg-zinc-900` calendar
 * with Czech month/weekday names from `Intl` (`cs-CZ`, weeks start
 * Monday), Today/Clear shortcuts, and an optional HH:mm row. Closes on
 * outside click and Escape; arrow keys move day focus inside the grid.
 */
export function DatePicker({
  value,
  onChange,
  label,
  error,
  placeholder,
  withTime = false,
  disabled = false,
  min,
  max,
  id,
  className = '',
}: DatePickerProps) {
  const generatedId = useId();
  const triggerId = id ?? generatedId;
  const popoverId = `${triggerId}-popover`;

  const { date: selectedDate, time } = parseValue(value);

  const [open, setOpen] = useState(false);
  const [viewYear, setViewYear] = useState(() => (selectedDate ?? new Date()).getFullYear());
  const [viewMonth, setViewMonth] = useState(() => (selectedDate ?? new Date()).getMonth());

  const rootRef = useRef<HTMLDivElement>(null);
  const gridRef = useRef<HTMLDivElement>(null);

  const monthFormatter = useMemo(
    () => new Intl.DateTimeFormat('cs-CZ', { month: 'long', year: 'numeric' }),
    [],
  );
  const displayFormatter = useMemo(() => new Intl.DateTimeFormat('cs-CZ'), []);
  const weekdayLabels = useMemo(() => {
    const formatter = new Intl.DateTimeFormat('cs-CZ', { weekday: 'short' });
    // 2024-01-01 is a Monday; weeks render Monday-first.
    return Array.from({ length: 7 }, (_, i) => formatter.format(new Date(2024, 0, 1 + i)));
  }, []);

  function openPopover(): void {
    if (disabled) return;
    const focus = selectedDate ?? new Date();
    setViewYear(focus.getFullYear());
    setViewMonth(focus.getMonth());
    setOpen(true);
  }

  function emit(date: Date | null, nextTime: string): void {
    if (!date) {
      onChange('');
      return;
    }
    onChange(withTime ? `${toIsoDate(date)}T${nextTime}` : toIsoDate(date));
  }

  function isOutOfRange(iso: string): boolean {
    return Boolean((min && iso < min) || (max && iso > max));
  }

  function selectDay(day: number): void {
    const date = new Date(viewYear, viewMonth, day);
    emit(date, time);
    if (!withTime) setOpen(false);
  }

  function moveMonth(delta: number): void {
    const next = new Date(viewYear, viewMonth + delta, 1);
    setViewYear(next.getFullYear());
    setViewMonth(next.getMonth());
  }

  function selectToday(): void {
    const today = new Date();
    setViewYear(today.getFullYear());
    setViewMonth(today.getMonth());
    if (!isOutOfRange(toIsoDate(today))) {
      emit(today, time);
      if (!withTime) setOpen(false);
    }
  }

  function onTimePartChange(part: 'hour' | 'minute', raw: string): void {
    const digits = raw.replace(/\D/g, '').slice(0, 2);
    const limit = part === 'hour' ? 23 : 59;
    const clamped = Math.min(limit, Number(digits || '0'));
    const [hour, minute] = time.split(':');
    const nextTime = part === 'hour' ? `${pad(clamped)}:${minute}` : `${hour}:${pad(clamped)}`;
    if (selectedDate) emit(selectedDate, nextTime);
  }

  function onGridKeyDown(event: React.KeyboardEvent<HTMLDivElement>): void {
    const deltas: Record<string, number> = {
      ArrowLeft: -1,
      ArrowRight: 1,
      ArrowUp: -7,
      ArrowDown: 7,
    };
    const delta = deltas[event.key];
    if (delta === undefined) return;
    const current = document.activeElement;
    const day = Number(current instanceof HTMLElement ? current.dataset.day : NaN);
    if (!day) return;
    event.preventDefault();
    const target = day + delta;
    const next = gridRef.current?.querySelector<HTMLButtonElement>(`button[data-day="${target}"]`);
    if (next) {
      next.focus();
    } else {
      moveMonth(delta > 0 ? 1 : -1);
    }
  }

  // Close when clicking/tapping anywhere outside the component.
  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
  const firstWeekday = (new Date(viewYear, viewMonth, 1).getDay() + 6) % 7;
  const todayIso = toIsoDate(new Date());
  const selectedIso = selectedDate ? toIsoDate(selectedDate) : null;
  const [hourValue, minuteValue] = time.split(':');

  const displayValue = selectedDate
    ? `${displayFormatter.format(selectedDate)}${withTime ? ` ${time}` : ''}`
    : '';

  return (
    <div className={`flex flex-col gap-1.5 ${className}`} ref={rootRef}>
      {label && (
        <label htmlFor={triggerId} className="text-sm font-medium text-zinc-300">
          {label}
        </label>
      )}
      <div className="relative">
        <button
          type="button"
          id={triggerId}
          disabled={disabled}
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? popoverId : undefined}
          onClick={() => (open ? setOpen(false) : openPopover())}
          onKeyDown={(event) => {
            if (event.key === 'Escape' && open) {
              event.preventDefault();
              setOpen(false);
            }
          }}
          className={`flex w-full items-center gap-2.5 rounded-lg border bg-zinc-900 py-2.5 pl-4 pr-10 text-left text-sm text-zinc-100 transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500 ${
            error
              ? 'border-error'
              : 'border-zinc-700 hover:border-zinc-500 focus-visible:border-brand-400'
          }`}
        >
          <span aria-hidden="true" className="shrink-0 text-zinc-500">
            <Icon name="calendar" size={16} />
          </span>
          <span className={`grow truncate ${displayValue ? '' : 'text-zinc-500'}`}>
            {displayValue || placeholder || t('ui.datePicker.placeholder')}
          </span>
        </button>
        {displayValue && !disabled ? (
          <button
            type="button"
            aria-label={t('ui.datePicker.clear')}
            onClick={() => emit(null, '00:00')}
            className="absolute right-3 top-1/2 -translate-y-1/2 rounded-full p-0.5 text-zinc-500 transition-colors hover:text-zinc-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
          >
            <Icon name="x" size={14} />
          </button>
        ) : (
          <span
            aria-hidden="true"
            className={`pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-zinc-500 ${open ? 'rotate-180' : ''}`}
          >
            <Icon name="chevronDown" size={16} />
          </span>
        )}

        {open && (
          <div
            id={popoverId}
            role="dialog"
            aria-label={label ?? t('ui.datePicker.open')}
            onKeyDown={(event) => {
              if (event.key === 'Escape') setOpen(false);
            }}
            className="absolute left-0 top-full z-30 mt-2 w-72 max-w-[calc(100vw-2rem)] rounded-lg border border-zinc-700 bg-zinc-900 p-3 elevated-shadow"
          >
            <div className="mb-2 flex items-center justify-between gap-2">
              <button
                type="button"
                aria-label={t('ui.datePicker.prevMonth')}
                onClick={() => moveMonth(-1)}
                className="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-800 hover:text-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
              >
                <Icon name="chevronLeft" size={16} />
              </button>
              <span aria-live="polite" className="text-sm font-semibold text-zinc-100">
                {capitalize(monthFormatter.format(new Date(viewYear, viewMonth, 1)))}
              </span>
              <button
                type="button"
                aria-label={t('ui.datePicker.nextMonth')}
                onClick={() => moveMonth(1)}
                className="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-800 hover:text-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
              >
                <Icon name="chevronRight" size={16} />
              </button>
            </div>

            <div className="grid grid-cols-7 gap-0.5">
              {weekdayLabels.map((weekday) => (
                <span
                  key={weekday}
                  className="flex h-8 items-center justify-center text-xs font-medium text-zinc-500"
                >
                  {weekday}
                </span>
              ))}
            </div>

            <div ref={gridRef} onKeyDown={onGridKeyDown} className="grid grid-cols-7 gap-0.5">
              {Array.from({ length: firstWeekday }, (_, i) => (
                <span key={`blank-${i}`} />
              ))}
              {Array.from({ length: daysInMonth }, (_, i) => {
                const day = i + 1;
                const iso = toIsoDate(new Date(viewYear, viewMonth, day));
                const isSelected = iso === selectedIso;
                const isToday = iso === todayIso;
                const outOfRange = isOutOfRange(iso);
                return (
                  <button
                    key={day}
                    type="button"
                    data-day={day}
                    disabled={outOfRange}
                    aria-pressed={isSelected}
                    onClick={() => selectDay(day)}
                    className={`flex h-8 w-full items-center justify-center rounded-lg text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40 ${
                      isSelected
                        ? 'bg-brand-500 font-semibold text-on-brand'
                        : outOfRange
                          ? 'cursor-not-allowed text-zinc-500'
                          : isToday
                            ? 'font-semibold text-brand-300 hover:bg-zinc-800'
                            : 'text-zinc-300 hover:bg-zinc-800 hover:text-zinc-50'
                    }`}
                  >
                    {day}
                  </button>
                );
              })}
            </div>

            {withTime && (
              <div className="mt-3 flex items-center gap-2 border-t border-zinc-800 pt-3">
                <span aria-hidden="true" className="text-zinc-500">
                  <Icon name="clock" size={16} />
                </span>
                <span className="text-sm text-zinc-400">{t('ui.datePicker.timeLabel')}</span>
                <span className="ml-auto flex items-center gap-1">
                  <input
                    inputMode="numeric"
                    aria-label={t('ui.datePicker.hourLabel')}
                    value={hourValue}
                    disabled={!selectedDate}
                    onChange={(event) => onTimePartChange('hour', event.target.value)}
                    className="w-12 rounded-lg border border-zinc-700 bg-zinc-900 px-2 py-1.5 text-center text-sm text-zinc-100 transition-colors focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500"
                  />
                  <span className="text-zinc-500">:</span>
                  <input
                    inputMode="numeric"
                    aria-label={t('ui.datePicker.minuteLabel')}
                    value={minuteValue}
                    disabled={!selectedDate}
                    onChange={(event) => onTimePartChange('minute', event.target.value)}
                    className="w-12 rounded-lg border border-zinc-700 bg-zinc-900 px-2 py-1.5 text-center text-sm text-zinc-100 transition-colors focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500"
                  />
                </span>
              </div>
            )}

            <div className="mt-3 flex items-center justify-between border-t border-zinc-800 pt-3">
              <button
                type="button"
                onClick={() => {
                  emit(null, '00:00');
                  setOpen(false);
                }}
                className="rounded-lg px-2 py-1 text-sm text-zinc-400 transition-colors hover:text-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
              >
                {t('ui.datePicker.clear')}
              </button>
              <button
                type="button"
                onClick={selectToday}
                className="rounded-lg px-2 py-1 text-sm font-medium text-brand-300 transition-colors hover:text-brand-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
              >
                {t('ui.datePicker.today')}
              </button>
            </div>
          </div>
        )}
      </div>
      {error && <p className="text-sm text-error">{error}</p>}
    </div>
  );
}
