'use client';

import { useEffect, useState } from 'react';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';
import { scrollToTop } from '@/lib/utils/scroll';

/** Scroll depth (px) past which the button appears — roughly one viewport. */
const REVEAL_AFTER_PX = 600;

/**
 * Floating "back to top" control for long scrolling pages (catalog).
 *
 * Hidden until the page is scrolled past {@link REVEAL_AFTER_PX} so it
 * never covers content on a short result set. The scroll listener is
 * passive and only flips a boolean, so it does no layout work per frame.
 * The scroll itself is {@link scrollToTop}, shared with catalog
 * pagination so both move the page the same way.
 */
export function ScrollToTop() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const update = (): void => setVisible(window.scrollY > REVEAL_AFTER_PX);
    update();
    window.addEventListener('scroll', update, { passive: true });
    return () => window.removeEventListener('scroll', update);
  }, []);

  if (!visible) {
    return null;
  }

  return (
    <button
      type="button"
      onClick={scrollToTop}
      aria-label={t('common.scroll_to_top')}
      className="fixed bottom-6 right-6 z-40 inline-flex h-11 w-11 items-center justify-center rounded-lg border border-zinc-700 bg-surface-elevated text-zinc-300 transition-colors hover:border-brand-line hover:text-brand-300"
    >
      <Icon name="chevronUp" size={18} />
    </button>
  );
}
