---
id: 0021
title: API versioning — URL-path versioning; OpenAPI per host; breaking changes require a new version
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0021 — API versioning

## Context

Once we go live, the frontend evolves at a different pace than the backend, and we may eventually have third-party consumers (a mobile app, a partner integration). We need a versioning scheme that:
- Lets us evolve without breaking existing consumers.
- Is visible in URLs (debuggable, cacheable).
- Plays well with NSwag-generated TypeScript clients.

## Decision

### URL-path versioning, one segment after the host audience

```
/api/v1/customer/orders
/api/v1/maker/products
/api/v1/admin/payouts
/api/v1/public/registry/lookup
```

Every API URL has a `v{N}` segment. `v1` is the only version at launch.

### What counts as "breaking"

A change is breaking iff it would cause an existing consumer to fail. Specifically:
- Removing a field from a response.
- Changing a field's type (string → number, nullable → required).
- Renaming a route or a field.
- Adding a required request field.
- Changing the meaning of a value (e.g. enum semantics).

Not breaking:
- Adding a new optional request field.
- Adding a new field to a response (consumers must tolerate extra fields).
- Adding a new endpoint.
- Adding a new optional enum value (consumers must tolerate unknown values; the contract DTO is `[JsonExtensionData]`-equipped to swallow).
- Performance changes that don't alter semantics.

### Breaking change → new version

When a breaking change is required:
1. Add the new endpoint at `/api/v2/...`.
2. Keep the `v1` endpoint working for a deprecation window — minimum **3 months**.
3. Mark `v1` deprecated in OpenAPI (`deprecated: true`) and in the `Sunset` HTTP response header.
4. After the deprecation window, remove `v1`.

The deprecation window can be longer for third-party-facing endpoints once we have them; 3 months is the floor for own-frontend.

### Frontend always pinned to one version

The frontend uses a single API version at a time. The NSwag-generated client carries the version in URLs. Upgrading the frontend's API version is a deliberate, atomic PR (regenerate client + update consumers).

### OpenAPI per host, per version

Each `Web.*` host emits `/openapi/v{N}.json` covering only that audience's routes. NSwag generates a separate TypeScript client per host (already established in ADR 0007). When `v2` lands:
- `Web.Customer` exposes `/openapi/v1.json` AND `/openapi/v2.json`.
- NSwag generates `customer-api.v1.ts` AND `customer-api.v2.ts`.
- Frontend imports `customer-api.v2.ts`. The `v1.ts` client exists only for transitional safety.

### Implementation

Use `Asp.Versioning.Mvc` (the modern ASP.NET Core API versioning library) with URL-segment routing:

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/customer/orders")]
[ApiVersion("1.0")]
public class OrdersControllerV1 : MakablesApiController
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrder.Command command, CancellationToken ct)
        => HandleResult(await Mediator.Send(command, ct));
}
```

When `v2` is needed, either decorate the same controller with `[ApiVersion("2.0")]` (if the route signature is unchanged) or create `OrdersControllerV2` (if the route signature changed). Mediator commands themselves may or may not be versioned — typically only the API layer is, with the v1 controller adapting old payloads to the latest command shape.

### Versioning of commands and DTOs in `Core.AppServices`

`Core.AppServices` commands/queries/DTOs are **not** versioned. Only the API boundary is versioned. If `v1` and `v2` need different request shapes, the `v1` controller maps the old payload to the latest command:

```csharp
public class OrdersControllerV1 : MakablesApiController
{
    [HttpPost]
    [ApiVersion("1.0")]
    public async Task<IActionResult> CreateLegacy([FromBody] CreateOrderV1Request body, CancellationToken ct)
    {
        var command = new CreateOrder.Command(
            ProductId: body.ProductId,
            Quantity: body.Quantity,
            ShippingMethod: body.ShippingMethod,
            // v1 used `pickupId`, v2 renamed to `pickupPointId`; map here:
            ZasilkovnaBranchId: body.PickupId,
            // ...
        );
        return HandleResult(await Mediator.Send(command, ct));
    }
}
```

This keeps `Core.AppServices` clean: only one shape per use case. The version map is at the edge.

### Deprecation responses

Endpoints in a deprecated version include:

```
Deprecation: true
Sunset: 2026-09-01T00:00:00Z
Link: <https://api.makables.cz/api/v2/customer/orders>; rel="successor-version"
```

Frontend's `apiFetch` logs a warning when these headers are present, so frontend developers see deprecation calls in the dev console.

### What is NOT versioned

- Webhooks (Comgate, Packeta, Resend) — they're inbound; the third party chooses the shape. We just adapt.
- Cron endpoints under `/api/v1/public/cron/...` — internal; we can change them freely.
- Auth endpoints (`/api/v1/public/auth/*`) — we control both sides; bumped only on breaking change.

## Alternatives considered

- **Header-based versioning** (`X-API-Version: 2`) — rejected. Less debuggable; caching harder; breaks naive curl experiments.
- **Media-type versioning** (`Accept: application/vnd.makables.v2+json`) — rejected. Same issues as headers plus complexity.
- **Query-param versioning** (`?v=2`) — rejected. Mixes versioning with filtering; ugly URLs; doesn't surface in routing tables.
- **No versioning, "we'll worry about it later"** — rejected. The cost of retrofitting versioning after live consumers exist is much higher than carrying `v1` from day one. Once we go live, changes are expensive (per user direction).

## Consequences

### Positive
- Clear, debuggable URLs.
- Deprecation is a documented lifecycle, not an emergency.
- Frontend version upgrades are atomic and reviewable.
- Future mobile / third-party consumers have a stable contract.

### Negative
- Every URL has an extra segment. Trivial cost.
- Maintaining two versions during the deprecation window requires keeping two controllers (or two `[ApiVersion]` decorations) alive. Mitigated by the v1-adapter-to-latest-command pattern.

## Compliance / verification

- Reviewer: every new controller/endpoint has an explicit `[ApiVersion]` attribute.
- Reviewer: removing or breaking a v1 endpoint requires a superseding ADR and a confirmed sunset date.
- Reviewer: the NSwag-generated client matches the version segment in the OpenAPI spec.
- CI: OpenAPI specs for `v1` are snapshot-tested; unintended changes flagged.

## Related

- Patterns: §A.21 NSwag client generation (per-host, per-version)
- ADR 0007 (per-audience hosts)
- ADR 0022 (next — NSwag pipeline mechanics)
