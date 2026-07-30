'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '@/components/ui/icon';

export interface DropdownOption {
  readonly value: string;
  readonly label: string;
}

interface DropdownProps {
  readonly options: ReadonlyArray<DropdownOption>;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label?: string;
  /** Rendered as the first option with an empty value ("any"). */
  readonly placeholder?: string;
  readonly error?: string;
  readonly disabled?: boolean;
  readonly id?: string;
  readonly className?: string;
}

/**
 * Custom select on the solid dark surface system (native `<select>`
 * popups can't be themed). Implements the WAI-ARIA combobox/listbox
 * pattern: ArrowUp/Down move the active option, Enter/Space select,
 * Escape/outside-click close, Home/End jump. This is THE select of the
 * design system — use it everywhere instead of a native `<select>`.
 */
export function Dropdown({
  options,
  value,
  onChange,
  label,
  placeholder,
  error,
  disabled = false,
  id,
  className = '',
}: DropdownProps) {
  const generatedId = useId();
  const triggerId = id ?? generatedId;
  const listboxId = `${triggerId}-listbox`;

  const allOptions: ReadonlyArray<DropdownOption> = placeholder
    ? [{ value: '', label: placeholder }, ...options]
    : options;

  const selectedIndex = Math.max(
    0,
    allOptions.findIndex((option) => option.value === value),
  );

  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(selectedIndex);

  const rootRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const selected = allOptions[selectedIndex];

  function openList() {
    if (disabled) return;
    setActiveIndex(selectedIndex);
    setOpen(true);
  }

  function selectAt(index: number) {
    const option = allOptions[index];
    if (option) onChange(option.value);
    setOpen(false);
  }

  function onTriggerKeyDown(event: React.KeyboardEvent<HTMLButtonElement>) {
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();
        if (!open) {
          openList();
        } else {
          const delta = event.key === 'ArrowDown' ? 1 : -1;
          setActiveIndex((current) => Math.min(allOptions.length - 1, Math.max(0, current + delta)));
        }
        break;
      case 'Home':
        if (open) {
          event.preventDefault();
          setActiveIndex(0);
        }
        break;
      case 'End':
        if (open) {
          event.preventDefault();
          setActiveIndex(allOptions.length - 1);
        }
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        if (open) {
          selectAt(activeIndex);
        } else {
          openList();
        }
        break;
      case 'Escape':
        if (open) {
          event.preventDefault();
          setOpen(false);
        }
        break;
      case 'Tab':
        setOpen(false);
        break;
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

  // Keep the active option in view while navigating with the keyboard.
  // Optional call: jsdom elements have no scrollIntoView.
  useEffect(() => {
    if (!open) return;
    listRef.current
      ?.querySelector<HTMLElement>(`[data-index="${activeIndex}"]`)
      ?.scrollIntoView?.({ block: 'nearest' });
  }, [open, activeIndex]);

  return (
    <div className="flex flex-col gap-1.5" ref={rootRef}>
      {label && (
        <label htmlFor={triggerId} className="text-sm font-medium text-zinc-400">
          {label}
        </label>
      )}
      <div className="relative">
        <button
          type="button"
          id={triggerId}
          role="combobox"
          aria-expanded={open}
          aria-haspopup="listbox"
          aria-controls={listboxId}
          disabled={disabled}
          onClick={() => (open ? setOpen(false) : openList())}
          onKeyDown={onTriggerKeyDown}
          className={`flex w-full items-center justify-between gap-2 rounded-xl border bg-zinc-900 px-4 py-2.5 text-left text-sm transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/30 disabled:cursor-not-allowed disabled:bg-zinc-800 disabled:opacity-50 ${
            error
              ? 'border-error text-zinc-100'
              : 'border-zinc-700 text-zinc-100 hover:border-zinc-500 focus-visible:border-brand-400'
          } ${className}`}
        >
          <span className={selected && selected.value !== '' ? 'truncate' : 'truncate text-zinc-500'}>
            {selected?.label ?? placeholder ?? ''}
          </span>
          <span
            aria-hidden="true"
            className={`shrink-0 text-zinc-500 transition-transform duration-200 ${open ? 'rotate-180' : ''}`}
          >
            <Icon name="chevronDown" size={16} />
          </span>
        </button>

        {open && (
          <ul
            ref={listRef}
            id={listboxId}
            role="listbox"
            aria-labelledby={triggerId}
            className="absolute inset-x-0 top-full z-30 mt-2 max-h-64 overflow-y-auto rounded-xl border border-zinc-700 bg-zinc-900 py-1.5 shadow-2xl shadow-black/50 motion-safe:animate-tooltip-in"
          >
            {allOptions.map((option, index) => {
              const isSelected = index === selectedIndex;
              const isActive = index === activeIndex;
              return (
                <li
                  key={`${option.value}-${index}`}
                  role="option"
                  aria-selected={isSelected}
                  data-index={index}
                  onPointerMove={() => setActiveIndex(index)}
                  onClick={() => selectAt(index)}
                  className={`flex cursor-pointer items-center justify-between gap-2 px-4 py-2 text-sm transition-colors ${
                    isActive ? 'bg-zinc-800 text-white' : 'text-zinc-400'
                  } ${isSelected ? 'text-brand-300' : ''}`}
                >
                  <span className="truncate">{option.label}</span>
                  {isSelected && (
                    <span aria-hidden="true" className="shrink-0 text-brand-400">
                      <Icon name="check" size={14} />
                    </span>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>
      {error && <p className="text-sm text-error">{error}</p>}
    </div>
  );
}
