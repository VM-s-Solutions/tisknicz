---
role: EmailProvider
kind: adapter
status: accepted
---

# EmailProvider

## Responsibility

Render a named template with given data for a recipient locale and submit the message to the upstream mail service.

## Collaborators

- **EmailTemplate** (reads: subject + body for given template + locale; templates compiled at build time)
- (Caller assembles the data — this role does not load entities)

## Knows

- The upstream service it talks to (Resend at launch)
- The templates directory structure (`templates/<code>/<locale>/`)
- Sender from-address per template (defaulting to platform default)

## Does NOT know

- Why the email is being sent
- Whether the recipient has unsubscribed (unsubscribe / consent is post-MVP)
- Locale resolution rules (caller passes the resolved locale)

## Interface

See ADR 0019. Method: `SendAsync(EmailMessage)` → `EmailSentReceipt`.

## Implementations

- **ResendEmailProvider** (`Infra.Clients/Resend/`)
- Future: SendGridEmailProvider, AwsSesEmailProvider

Invoked exclusively from the outbox processor (`ProcessOutboxFunction` for `email.send` events). Never called directly from handlers.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Email/IEmailProvider.cs`.

## Related

- ADRs: 0019 (this role's defining ADR), 0020 (outbox is the call site)
- Roles: `outbox` (system role)
