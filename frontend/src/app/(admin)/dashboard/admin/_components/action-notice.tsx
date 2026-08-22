'use client';

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { Alert } from '@/components/ui/alert';

interface ActionNotice {
  readonly report: (message: string) => void;
}

const Ctx = createContext<ActionNotice | null>(null);

/**
 * Success notices that outlive the row that produced them (T-0176,
 * audit ADM-M5). The outbox retry/acknowledge results rendered INSIDE
 * the row, and both actions remove the event from the stalled set — so
 * the `router.refresh()` that followed unmounted the row and its
 * message (including the retry count) before it could be read. The row
 * just vanished with no confirmation of what happened.
 *
 * The provider sits ABOVE the list, so it survives the refresh; rows
 * report into it through {@link useActionNotice}.
 */
export function ActionNoticeProvider({ children }: { readonly children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null);
  const report = useCallback((next: string) => setMessage(next), []);
  const value = useMemo(() => ({ report }), [report]);

  return (
    <Ctx.Provider value={value}>
      {message ? (
        <div className="mb-4">
          <Alert variant="success">{message}</Alert>
        </div>
      ) : null}
      {children}
    </Ctx.Provider>
  );
}

/**
 * Report a success message to the surrounding {@link ActionNoticeProvider}.
 * Outside a provider it is a no-op, so a row can still render standalone
 * (and in tests) without scaffolding.
 */
export function useActionNotice(): ActionNotice {
  return useContext(Ctx) ?? { report: () => {} };
}
