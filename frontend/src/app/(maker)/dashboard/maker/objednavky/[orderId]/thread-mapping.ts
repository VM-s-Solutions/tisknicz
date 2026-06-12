import type { OrderMessagesPage } from '@/components/shared/order-message-thread';
import type { OrderMessagesPageData } from '@/lib/api-client-helpers/maker-orders';

/**
 * Adapter from the maker-host wire page (T-0079
 * `PagedData<OrderMessageDto>`) to the audience-agnostic shape the
 * shared `OrderMessageThread` consumes (T-0086b §C seam; T-0087b A.2
 * lock — same component, maker wiring). Server-safe module so both the
 * SSR `initialPage` prefetch (page.tsx) and the client callback wrapper
 * (order-thread-client.tsx) share one mapping.
 */
export function toThreadMessagesPage(data: OrderMessagesPageData): OrderMessagesPage {
  return {
    items: data.items.map((item) => ({
      id: item.id,
      authorName: item.authorName,
      body: item.body,
      createdAt: item.createdAt,
      isMine: item.isMine,
    })),
    page: data.page,
    totalCount: data.totalCount,
    hasNextPage: data.hasNextPage ?? false,
  };
}
