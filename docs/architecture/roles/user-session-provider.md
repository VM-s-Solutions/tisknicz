---
role: UserSessionProvider (IUserSessionProvider)
kind: application-service
status: accepted
---

# UserSessionProvider

## Responsibility

Read-side abstraction over the current authenticated user, sourced from
JWT claims of the inbound HTTP request (Web hosts) or the configured
"system" identity (Functions / cron). Lets domain services
(`AuditableSaveChangesInterceptor`, the audit pipeline behavior, any
handler that needs "who am I") avoid coupling to ASP.NET Core's
`HttpContext`.

## Collaborators

- (Wraps the inbound request's identity; itself a leaf.)

## Knows

- The current authenticated user's id (ULID), email, and primary country
  code (from JWT claims).

## Does NOT know

- How authentication happens (`AuthService` orchestrates).
- Authorization decisions ("can this user do X?" — that's `[Authorize]`
  + ADR 0013 scoping).
- Whether the request has a body, headers, or any other HTTP-shaped
  detail.

## Lifecycle

- **Created by:** DI container; scoped to the request in Web hosts
  (reads `IHttpContextAccessor`); singleton "system" instance in
  Functions / cron.
- **Modified by:** never (it reads, never writes).
- **Destroyed by:** request scope ends (Web) / never (Functions).

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Common/IUserSessionProvider.cs`
(moved here from `Core.AppServices.Abstractions` because the EF Core
audit interceptor in `Infra.Database` needs it, and `Infra.*` does not
reference `Core.AppServices` per ADR 0001).

Impls (added in T-0008/T-0009 — DI wiring):
- `HttpContextUserSessionProvider` — Web hosts, reads JWT claims via
  `IHttpContextAccessor`.
- `SystemUserSessionProvider` — Functions/cron, returns "system" / null.

## Notable contracts

- `GetUserId()` returns `null` for anonymous callers. The audit
  interceptor substitutes `"system"` when null.
- All three accessors are independent — anonymous callers see `null` from
  all three; a logged-in customer sees all three populated; a system
  caller (cron) may return `"system"` for `GetUserId()` and `null` for
  the others.

## Related

- ADRs: 0001 (relocation rationale), 0012 (authentication — JWT claims
  populated by `AuthService`), 0014 (admin audit log reads the user id)
- Roles: `auth-service`, `auditable-entity`
