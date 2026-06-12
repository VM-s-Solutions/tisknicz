'use client';

import { useCallback } from 'react';
import {
  OrderMessageThread,
  type OrderMessagesPage,
} from '@/components/shared/order-message-thread';
import {
  getMakerOrderMessages,
  markMakerOrderMessagesRead,
  postMakerOrderMessage,
} from '@/lib/api-client-helpers/maker-orders';
import type { ApiError, Result } from '@/lib/runtime/result';
import { ok } from '@/lib/runtime/result';
import { toThreadMessagesPage } from './thread-mapping';

/**
 * Maker-audience consumer of the shared `OrderMessageThread` (T-0087b
 * A.2 lock — the T-0086b adapter seam, no fork). Functions can't cross
 * the server→client boundary, so the server page renders this thin
 * wrapper which builds the three `Result`-returning callbacks from the
 * maker host's helpers client-side. Mark-read fires on render inside
 * the shared component, zeroing `maker_unread_message_count` so the
 * T-0087a list badge clears on back-navigation. Callbacks are
 * `useCallback`-stable per the component contract (poll-effect
 * identity).
 */

interface OrderThreadClientProps {
  readonly orderId: string;
  readonly initialPage: OrderMessagesPage;
  readonly canPost: boolean;
}

export function OrderThreadClient({ orderId, initialPage, canPost }: OrderThreadClientProps) {
  const fetchMessages = useCallback(
    async (page: number): Promise<Result<OrderMessagesPage, ApiError>> => {
      const result = await getMakerOrderMessages(orderId, page);
      return result.success ? ok(toThreadMessagesPage(result.value)) : result;
    },
    [orderId],
  );

  const postMessage = useCallback(
    async (body: string): Promise<Result<unknown, ApiError>> =>
      postMakerOrderMessage(orderId, body),
    [orderId],
  );

  const markRead = useCallback(
    async (): Promise<Result<unknown, ApiError>> => markMakerOrderMessagesRead(orderId),
    [orderId],
  );

  return (
    <OrderMessageThread
      orderId={orderId}
      initialPage={initialPage}
      canPost={canPost}
      fetchMessages={fetchMessages}
      postMessage={postMessage}
      markRead={markRead}
    />
  );
}
