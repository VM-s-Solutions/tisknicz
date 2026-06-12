'use client';

import { useCallback } from 'react';
import {
  OrderMessageThread,
  type OrderMessagesPage,
} from '@/components/shared/order-message-thread';
import {
  getOrderMessages,
  markOrderMessagesRead,
  postOrderMessage,
} from '@/lib/api-client-helpers/orders-client';
import type { ApiError, Result } from '@/lib/runtime/result';
import { ok } from '@/lib/runtime/result';
import { toThreadMessagesPage } from './thread-mapping';

/**
 * Customer-audience consumer of the shared `OrderMessageThread`
 * (T-0086b §C "injected callbacks"). Functions can't cross the
 * server→client boundary, so the server page renders this thin wrapper
 * which builds the three `Result`-returning callbacks from the customer
 * host's helpers client-side. T-0087b ships the maker equivalent with
 * its own helpers — the shared component never learns which audience
 * it serves. Callbacks are `useCallback`-stable per the component
 * contract (poll-effect identity).
 */

interface OrderThreadClientProps {
  readonly orderId: string;
  readonly initialPage: OrderMessagesPage;
  readonly canPost: boolean;
}

export function OrderThreadClient({ orderId, initialPage, canPost }: OrderThreadClientProps) {
  const fetchMessages = useCallback(
    async (page: number): Promise<Result<OrderMessagesPage, ApiError>> => {
      const result = await getOrderMessages(orderId, page);
      return result.success ? ok(toThreadMessagesPage(result.value)) : result;
    },
    [orderId],
  );

  const postMessage = useCallback(
    async (body: string): Promise<Result<unknown, ApiError>> => postOrderMessage(orderId, body),
    [orderId],
  );

  const markRead = useCallback(
    async (): Promise<Result<unknown, ApiError>> => markOrderMessagesRead(orderId),
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
