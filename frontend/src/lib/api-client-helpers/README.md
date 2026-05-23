# `lib/api-client-helpers/` — call-site wrappers around the NSwag clients

The generated clients in `lib/api-client/*-api.v1.ts` are auto-generated and
must not be hand-edited (per ADR 0022). Anything that depends on the
generated client — attaching auth headers, mapping responses into the
frontend's `Result<T, ApiError>` shape, retry policies, idempotency keys —
lives here.

## Convention

One file per feature surface, named `<feature>-client.ts`. Each exports
async functions that:

1. Resolve the current `Session` (from `lib/auth/`).
2. Call the generated client through `apiFetch` (in `lib/runtime/`).
3. Return a `Result<TValue, ApiError>` so call sites can narrow without
   try/catch.

```ts
// example: lib/api-client-helpers/orders-client.ts
import { apiFetch } from '@/lib/runtime';
import type { Result, ApiError } from '@/lib/runtime';

export async function getCustomerOrders(): Promise<Result<OrderListItem[], ApiError>> {
  return apiFetch<OrderListItem[]>('customer', '/api/v1/customer/orders');
}
```

Phase 1 ships only the runtime + scaffold; the first real helper lands
with T-0035 (auth pages) once T-0020/T-0022 have produced real endpoints
and the generated client exists.
