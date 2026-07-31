'use client';

import { type FormEvent, useEffect, useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError, Result } from '@/lib/runtime/result';
import { formatDateTime } from '@/lib/utils/dates';
import { ORDER_MESSAGE_MAX_LENGTH } from '@/lib/utils/validation';

/**
 * Shared, audience-agnostic order-message thread (T-0086b Q6 lock;
 * T-0087b reuses it verbatim on the maker detail page). The component
 * knows how to RENDER a thread; each consumer knows how to TALK to its
 * host — the three data callbacks are injected and must return
 * `Result<…, ApiError>` (no `lib/api-client-helpers` import here, ever).
 *
 * Contract notes for consumers:
 * - Callbacks must be referentially stable (wrap in `useCallback` keyed
 *   on the order id) — identity churn re-arms the poll effect. The
 *   cleanup clears the interval first either way, so churn can never
 *   double-poll, but stability keeps the timer cadence honest.
 * - `initialPage` is the SSR-prefetched page 1 (patterns.md B.1 — the
 *   thread is data-complete on first paint; no client fetch waterfall).
 * - `canPost` carries the T-0079 PendingPayment guard: `false` hides
 *   the post box and renders the info note instead. Driven by the
 *   consumer's `state` read, never inspected here.
 *
 * Polling (the Q5 exception — the ONLY polling surface on the
 * platform): one page-1 refetch every {@link POLL_INTERVAL_MS} while
 * the tab is visible; paused on `visibilitychange` hidden; one
 * immediate refetch + resume on return; interval cleared on unmount
 * (payment-poll-client.tsx discipline).
 *
 * Mark-read fires on mount and re-fires after any refetch that delivers
 * new counterparty messages — keeps the dashboard badge at 0 for the
 * whole visit; the backend call is idempotent (T-0079).
 */

/** Poll spacing — Q5 exception (grooming lock). Named so a future tune is one line. */
export const POLL_INTERVAL_MS = 30_000;

export interface OrderMessage {
  readonly id: string;
  readonly authorName: string;
  readonly body: string;
  /** ISO 8601 — formatted via lib/utils/dates. */
  readonly createdAt: string;
  readonly isMine: boolean;
}

export interface OrderMessagesPage {
  readonly items: readonly OrderMessage[];
  readonly page: number;
  readonly totalCount: number;
  readonly hasNextPage: boolean;
}

export interface OrderMessageThreadProps {
  readonly orderId: string;
  /** SSR-fetched page 1 — first paint is data-complete (B.1). */
  readonly initialPage: OrderMessagesPage;
  /** `false` on PendingPayment (T-0079 guard) — post box hidden, info note shown. */
  readonly canPost: boolean;
  readonly fetchMessages: (page: number) => Promise<Result<OrderMessagesPage, ApiError>>;
  readonly postMessage: (body: string) => Promise<Result<unknown, ApiError>>;
  readonly markRead: () => Promise<Result<unknown, ApiError>>;
}

export function OrderMessageThread({
  orderId,
  initialPage,
  canPost,
  fetchMessages,
  postMessage,
  markRead,
}: OrderMessageThreadProps) {
  // Newest-first flat list (page 1 head, older pages appended below).
  const [messages, setMessages] = useState<readonly OrderMessage[]>(initialPage.items);
  const [oldestLoadedPage, setOldestLoadedPage] = useState(initialPage.page);
  const [hasOlder, setHasOlder] = useState(initialPage.hasNextPage);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refreshInFlightRef = useRef(false);

  // Every message id ever shown (NEW-2 fold): fresh-sets are derived
  // from this ref OUTSIDE the setMessages updaters so the updaters stay
  // pure (React may defer or double-invoke them — deciding mark-read
  // inside one would silently skip the re-fire) and the dedupe decision
  // is deterministic at dispatch time. Invariant: every add-path filters
  // against the ref and records its additions here BEFORE dispatching,
  // so `messages` is always a subset of this set.
  const knownIdsRef = useRef<Set<string>>(new Set(initialPage.items.map((m) => m.id)));

  // The sanctioned poll effect (Q6 lock — mirror of the T-0085 poller
  // discipline: single interval, visibility-pause, unmount cleanup).
  // Initial data came from the server as a prop; this timer only pulls
  // DELTAS, so it is not `useEffect` data fetching in the B.1 sense.
  useEffect(() => {
    let disposed = false;
    let intervalId: number | null = null;

    const refresh = async (markEvenWithoutNews: boolean) => {
      if (disposed || refreshInFlightRef.current) return;
      refreshInFlightRef.current = true;
      const result = await fetchMessages(1);
      refreshInFlightRef.current = false;
      if (disposed) return;
      if (!result.success) {
        // Transient poll errors are swallowed — the next tick retries.
        return;
      }
      // Diff against knownIdsRef OUTSIDE the updater (purity — see the
      // ref's comment) so counterpartyNews is decided deterministically
      // before dispatch.
      const known = knownIdsRef.current;
      const fresh = result.value.items.filter((m) => !known.has(m.id));
      const counterpartyNews = fresh.some((m) => !m.isMine);
      if (fresh.length > 0) {
        for (const m of fresh) known.add(m.id);
        setMessages((prev) => [...fresh, ...prev]);
      }
      // Serialize refetch-then-mark-read (T-0086b risk note) so the
      // counter reset always covers everything just rendered.
      if (markEvenWithoutNews || counterpartyNews) {
        await markRead();
      }
    };

    const start = () => {
      if (intervalId === null) {
        intervalId = window.setInterval(() => {
          void refresh(false);
        }, POLL_INTERVAL_MS);
      }
    };
    const stop = () => {
      if (intervalId !== null) {
        window.clearInterval(intervalId);
        intervalId = null;
      }
    };

    const onVisibilityChange = () => {
      if (document.hidden) {
        stop();
      } else {
        // One immediate refetch on return, then resume the cadence.
        void refresh(false);
        start();
      }
    };

    // Mount: the user is reading the thread — clear the unread counter
    // unconditionally (mark-read-on-render, Q6 lock), then start polling.
    void refresh(true);
    document.addEventListener('visibilitychange', onVisibilityChange);
    if (!document.hidden) {
      start();
    }

    return () => {
      disposed = true;
      stop();
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, [fetchMessages, markRead]);

  async function handleLoadOlder() {
    if (loadingOlder) return;
    setLoadingOlder(true);
    setError(null);
    const result = await fetchMessages(oldestLoadedPage + 1);
    setLoadingOlder(false);
    if (!result.success) {
      setError(resolveErrorMessage(result.error));
      return;
    }
    const incoming = result.value;
    // Pagination windows shift as new messages arrive; dedupe by id
    // against knownIdsRef (outside the updater — purity) and append only
    // genuinely older rows at the tail.
    const known = knownIdsRef.current;
    const older = incoming.items.filter((m) => !known.has(m.id));
    if (older.length > 0) {
      for (const m of older) known.add(m.id);
      setMessages((prev) => [...prev, ...older]);
    }
    setOldestLoadedPage(incoming.page);
    setHasOlder(incoming.hasNextPage);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const body = draft.trim();
    if (body.length === 0 || sending) return;
    setSending(true);
    setError(null);
    const result = await postMessage(body);
    if (!result.success) {
      setSending(false);
      setError(resolveErrorMessage(result.error));
      return;
    }
    // Server-authoritative append (T-0086b Option E rejection): the new
    // message arrives via the page-1 refetch with its real id/timestamp.
    setDraft('');
    const refreshed = await fetchMessages(1);
    if (refreshed.success) {
      const known = knownIdsRef.current;
      const fresh = refreshed.value.items.filter((m) => !known.has(m.id));
      if (fresh.length > 0) {
        for (const m of fresh) known.add(m.id);
        setMessages((prev) => [...fresh, ...prev]);
      }
    }
    setSending(false);
  }

  return (
    <div className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold text-white">{t('orderMessages.heading')}</h2>

      {error ? <Alert variant="error">{error}</Alert> : null}

      {canPost ? (
        <form onSubmit={handleSubmit} className="flex flex-col gap-2">
          <Textarea
            id={`order-thread-post-${orderId}`}
            label={t('orderMessages.postLabel')}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder={t('orderMessages.postPlaceholder')}
            maxLength={ORDER_MESSAGE_MAX_LENGTH}
            rows={3}
            disabled={sending}
          />
          <div className="flex items-center justify-between gap-3">
            <span className="text-xs text-zinc-500">
              {t('orderMessages.counter', {
                count: draft.length,
                max: ORDER_MESSAGE_MAX_LENGTH,
              })}
            </span>
            <Button type="submit" loading={sending} disabled={draft.trim().length === 0}>
              {sending ? t('orderMessages.sending') : t('orderMessages.send')}
            </Button>
          </div>
        </form>
      ) : (
        <Alert variant="info">{t('orderMessages.pendingPaymentNote')}</Alert>
      )}

      {messages.length === 0 ? (
        <p className="rounded-xl border border-dashed border-zinc-800 px-4 py-6 text-center text-sm text-zinc-500">
          {t('orderMessages.empty')}
        </p>
      ) : (
        <ul className="flex flex-col gap-3">
          {messages.map((message) => (
            <li
              key={message.id}
              className={`flex flex-col gap-1 rounded-xl border px-4 py-3 ${
                message.isMine
                  ? 'ml-8 items-end border-brand-400/20 bg-brand-400/5 sm:ml-16'
                  : 'mr-8 border-zinc-800 bg-zinc-800/50 sm:mr-16'
              }`}
            >
              <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-xs text-zinc-500">
                <span className="break-words font-semibold text-zinc-400">{message.authorName}</span>
                <span>{formatDateTime(message.createdAt)}</span>
              </div>
              <p className="max-w-full whitespace-pre-wrap break-words text-sm text-zinc-200">{message.body}</p>
            </li>
          ))}
        </ul>
      )}

      {hasOlder ? (
        <div className="flex justify-center">
          <Button
            type="button"
            variant="outline"
            size="sm"
            loading={loadingOlder}
            onClick={() => void handleLoadOlder()}
          >
            {loadingOlder ? t('orderMessages.loadingOlder') : t('orderMessages.loadOlder')}
          </Button>
        </div>
      ) : null}
    </div>
  );
}
