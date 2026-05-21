---
id: 0006
title: Lightweight DI container with per-request scope
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0006 — Lightweight DI container with per-request scope

## Context
Handlers depend on repositories and services through interfaces (`patterns.md` §8). Something has to wire concrete implementations to those interfaces per request. Next.js Server Components and Route Handlers run per request and carry their own auth context, so repositories must be per-request — they hold a per-request Supabase client.

We need a wiring approach that is:
- explicit enough for review,
- ergonomic enough that feature authors don't write the same wiring code in every Route Handler,
- compatible with per-request lifetime,
- testable (mocks injected in unit tests),
- familiar to the .NET-trained user.

## Decision

Use **`tsyringe`** (or equivalent decorator-based DI container) with a per-request child container.

### Layout

```
src/lib/runtime/
├── di/
│   ├── container.ts          # root container, registers infra implementations
│   ├── tokens.ts             # injection tokens (one per interface)
│   ├── scope.ts              # createRequestScope(req) — child container per request
│   └── wiring/
│       ├── repositories.ts   # registers all Supabase repository tokens
│       ├── adapters.ts       # registers payment/shipping/registry/email adapters
│       └── services.ts       # registers domain services (pricing, numbering, etc.)
```

### Tokens

Interfaces in `domain/` are paired with an injection token:

```ts
// src/lib/runtime/di/tokens.ts
export const TOKENS = {
  // Repositories
  OrderRepository:         Symbol.for('IOrderRepository'),
  MakerRepository:         Symbol.for('IMakerRepository'),
  ProductRepository:       Symbol.for('IProductRepository'),
  CountryConfigRepository: Symbol.for('ICountryConfigRepository'),
  // Adapters
  PaymentProvider:         Symbol.for('PaymentProvider'),     // resolved per-country
  ShippingCarrier:         Symbol.for('ShippingCarrier'),
  CompanyRegistry:         Symbol.for('CompanyRegistry'),
  EmailProvider:           Symbol.for('EmailProvider'),
  // Services
  PricingService:          Symbol.for('IPricingService'),
  Logger:                  Symbol.for('ILogger'),
  Clock:                   Symbol.for('IClock'),
  UnitOfWork:              Symbol.for('IUnitOfWork'),
} as const;
```

### Per-request scope

`makeContext(req)` creates a child container scoped to the request:

```ts
export const makeContext = async (req: NextRequest): Promise<HandlerContext> => {
  const supabase = await createServerClient(req);              // per-request Supabase
  const scope    = container.createChildContainer();
  scope.register(TOKENS.SupabaseClient, { useValue: supabase });
  scope.register(TOKENS.RequestId,      { useValue: crypto.randomUUID() });
  return { di: scope, ... };
};
```

Repository registrations are constructor-injected with the request's Supabase client:

```ts
@injectable()
export class SupabaseOrderRepository implements IOrderRepository {
  constructor(@inject(TOKENS.SupabaseClient) private db: SupabaseClient) {}
  // ...
}
```

### Feature wiring

A feature exposes a `buildXxxDeps(scope)` function that resolves its concrete dependencies:

```ts
// src/lib/features/orders/create-order.ts
export const buildCreateOrderDeps = (scope: DependencyContainer): CreateOrderDeps => ({
  orderRepo:     scope.resolve(TOKENS.OrderRepository),
  productRepo:   scope.resolve(TOKENS.ProductRepository),
  countryConfig: scope.resolve(TOKENS.CountryConfigRepository),
  pricing:       scope.resolve(TOKENS.PricingService),
});
```

Route Handler:

```ts
export async function POST(req: NextRequest) {
  const ctx = await makeContext(req);
  const pipeline = compose(...)(handler(buildCreateOrderDeps(ctx.di)));
  return handleResult(await pipeline(await req.json(), ctx));
}
```

### Provider selection (multi-country)

Provider adapters that vary per country are resolved by a factory, not a fixed token:

```ts
scope.register(TOKENS.PaymentProvider, {
  useFactory: (c) => async (countryCode: string) => {
    const config = await c.resolve<ICountryConfigRepository>(TOKENS.CountryConfigRepository).getByCode(countryCode);
    return paymentProviderRegistry[config.defaultPaymentProvider];
  },
});
```

### Testing

In unit tests, a feature is invoked with a hand-built `Deps` object — no container needed:

```ts
const deps: CreateOrderDeps = {
  orderRepo:     mockOrderRepo,
  productRepo:   mockProductRepo,
  countryConfig: mockCountryConfig,
  pricing:       mockPricing,
};
const result = await handler(deps)(input, ctx);
```

This is the key reason features take a `deps` object instead of resolving tokens themselves: tests bypass DI entirely.

## Alternatives considered

- **Manual per-request factory functions, no container** — rejected by user. Works fine at small scale; becomes a lot of repetitive wiring once we have 30+ features × 5–10 adapters.
- **Module-level singletons with method-arg client** — rejected. Awkward when a repo has multiple deps; loses per-request constructor benefits.
- **`inversify`** — rejected. Heavier than `tsyringe`, more decorators, more concepts.
- **`awilix`** — viable alternative; chose `tsyringe` for closer decorator ergonomics to the user's .NET DI experience.

## Consequences

- **Positive:** familiar to the .NET-trained user (constructor injection with decorators).
- **Positive:** per-request scope is explicit and bounded — no risk of leaking a Supabase client between requests.
- **Positive:** mocking in tests bypasses the container — no test-only DI configuration.
- **Negative:** adds `tsyringe` + `reflect-metadata` dependencies. Mitigation: both are tiny and stable.
- **Negative:** decorators require `experimentalDecorators` + `emitDecoratorMetadata` in `tsconfig`. Mitigation: standard Next.js + tsyringe pattern; one-time tsconfig change.
- **Negative:** developers can resolve tokens directly from `scope` inside a handler, bypassing the `deps` discipline. Mitigation: ESLint rule `no-restricted-imports` blocks `tsyringe` outside `src/lib/runtime/`.

## Compliance / verification

- Reviewer checklist: features take a `deps` object; no `scope.resolve()` calls inside handlers.
- Reviewer checklist: every interface in `domain/` has a paired token in `runtime/di/tokens.ts`.
- ESLint rule (SecOps to add): `tsyringe` may only be imported under `src/lib/runtime/`.
- Test convention: features unit-tested with hand-built `deps`, no container.

## Related
- Patterns: §6 Feature file structure, §8 Repository pattern, §15 Provider adapter pattern
- Depends on: ADR 0001 (layering)
- Will be referenced by: every Route Handler ticket
