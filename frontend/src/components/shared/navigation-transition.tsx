'use client';

import { useRouter } from 'next/navigation';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useTransition,
  type ReactNode,
} from 'react';
import { scrollToTop } from '@/lib/utils/scroll';

interface NavigateOptions {
  /** `replace` instead of `push`. Default push — back should undo the change. */
  readonly replace?: boolean;
  /** Scroll to the top AFTER the navigation's data arrived (not on click). */
  readonly scrollTop?: boolean;
}

interface NavigationTransition {
  readonly pending: boolean;
  readonly navigate: (url: string, options?: NavigateOptions) => void;
}

const Ctx = createContext<NavigationTransition | null>(null);

/**
 * Shared pending-state for URL-driven refetches (T-0170, audit PUB-H2).
 * Same-segment searchParam navigation does NOT re-show `loading.tsx`, so
 * a filter or pagination click used to freeze the old list with zero
 * feedback until the SSR round trip streamed in. Controls inside this
 * provider navigate through `useNavigationTransition().navigate`, and
 * anything wrapped in {@link TransitionDim} dims while the transition is
 * in flight. `scrollTop` fires once the data has actually arrived —
 * scrolling on click teleported the reader to the top of the OLD page.
 *
 * The `children` prop keeps server-rendered subtrees server-rendered;
 * only the provider itself is a client component.
 */
export function NavigationTransitionProvider({ children }: { readonly children: ReactNode }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const scrollAfterRef = useRef(false);
  const wasPendingRef = useRef(false);

  useEffect(() => {
    if (wasPendingRef.current && !pending && scrollAfterRef.current) {
      scrollAfterRef.current = false;
      scrollToTop();
    }
    wasPendingRef.current = pending;
  }, [pending]);

  const navigate = useCallback(
    (url: string, options?: NavigateOptions) => {
      if (options?.scrollTop) scrollAfterRef.current = true;
      startTransition(() => {
        if (options?.replace) router.replace(url, { scroll: false });
        else router.push(url, { scroll: false });
      });
    },
    [router],
  );

  const value = useMemo(() => ({ pending, navigate }), [pending, navigate]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/**
 * Consumer for controls that drive URL state. Outside a provider it
 * degrades to direct router navigation with no shared pending state
 * (standalone usage and unit tests keep working).
 */
export function useNavigationTransition(): NavigationTransition {
  const ctx = useContext(Ctx);
  const router = useRouter();
  const fallback = useMemo<NavigationTransition>(
    () => ({
      pending: false,
      navigate: (url, options) => {
        if (options?.replace) router.replace(url, { scroll: false });
        else router.push(url, { scroll: false });
        if (options?.scrollTop) scrollToTop();
      },
    }),
    [router],
  );
  return ctx ?? fallback;
}

/**
 * Dims its children while a navigation started through the surrounding
 * provider is in flight. Purely presentational — pointer events are
 * disabled so a stale grid can't collect clicks mid-swap.
 */
export function TransitionDim({
  children,
  className = '',
}: {
  readonly children: ReactNode;
  readonly className?: string;
}) {
  const { pending } = useNavigationTransition();
  return (
    <div
      aria-busy={pending}
      className={`transition-opacity duration-200 ${pending ? 'pointer-events-none opacity-50' : ''} ${className}`}
    >
      {children}
    </div>
  );
}
