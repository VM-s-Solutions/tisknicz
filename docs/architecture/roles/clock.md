---
role: Clock (IClock)
kind: domain-service
status: accepted
---

# Clock

## Responsibility

Provide the current instant (`DateTimeOffset.UtcNow`) for any code that
needs to read "now". Exists so that time-dependent code is testable
(fake clocks in unit tests can return a fixed instant).

## Collaborators

- (None — it's a leaf abstraction.)

## Knows

- The current UTC instant.

## Does NOT know

- Time zones (callers convert on display).
- Calendar arithmetic (business hours, working days — those live in
  domain services that consume `IClock`).
- Whether the host is "really" up to date (NTP / system clock skew is the
  platform's problem, not this abstraction's).

## Lifecycle

- **Created by:** DI container as singleton.
- **Modified by:** never (stateless).
- **Destroyed by:** never.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Common/IClock.cs`.
Impl: `backend/src/Makables.Infra.Common/Time/SystemClock.cs`.

## Related

- ADRs: 0011 (audit columns) — interceptor consumes `IClock`
- Roles: `auditable-entity` (implicit in patterns §A.11)
