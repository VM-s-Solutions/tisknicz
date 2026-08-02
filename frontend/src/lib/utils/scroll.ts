/**
 * Scrolls the window back to the top, smoothly unless the user asked for
 * reduced motion.
 *
 * Shared by the floating "back to top" control and by catalog pagination
 * so both feel the same. The motion is a navigation aid (it shows the
 * page moved rather than teleporting), not decoration — hence the
 * `prefers-reduced-motion` fallback to an instant jump instead of
 * dropping the scroll entirely.
 *
 * Client-only: callers must be in a Client Component or an event handler.
 */
export function scrollToTop(): void {
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  window.scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' });
}
