# Domain roles catalog

This catalog is the **what** of the Makables domain model. Each role file describes one object — aggregate, value object, domain service, repository, or adapter — by its responsibility, collaborators, what it knows, and explicitly what it does NOT know.

See [ADR 0015 — Responsibility-Driven Design](../../adr/0015-responsibility-driven-design.md) for the discipline.

## Aggregates

| Role | File | Responsibility (one line) |
|---|---|---|
| Order | [order.md](./order.md) | Capture a customer's intent to purchase and track it through delivery |
| Maker | [maker.md](./maker.md) | Represent a registered Czech business that produces goods for the platform |
| Product | [product.md](./product.md) | A catalog entry a maker offers for sale |
| Invoice | [invoice.md](./invoice.md) | Legal record of a payment between two parties |
| PayoutBatch | [payout-batch.md](./payout-batch.md) | Weekly grouping of maker payouts + platform fee invoices |
| User | [user.md](./user.md) | Identity behind every action on the platform |
| Category | [category.md](./category.md) | Reference data for product categorization |
| Review | [review.md](./review.md) | Customer rating + comment for a delivered order |
| OrderMessage | [order-message.md](./order-message.md) | One message in the customer ↔ maker order thread |
| Dispute | [dispute.md](./dispute.md) | Why an order detoured to `Disputed` and how admin resolved it |
| CountryConfiguration | [country-configuration.md](./country-configuration.md) | Control plane for per-country variation |
| AdminAuditLogEntry | [admin-audit-log-entry.md](./admin-audit-log-entry.md) | Append-only record of an admin write |

## Value objects

| Role | File | Responsibility |
|---|---|---|
| Money | [money.md](./money.md) | Monetary amount in a specific currency, with safe arithmetic |
| Address | [address.md](./address.md) | Structured postal location with optional coordinates |

## Domain services

| Role | File | Responsibility |
|---|---|---|
| OrderPricing | [order-pricing.md](./order-pricing.md) | Compute customer total and maker payout |
| OrderNumbering | [order-numbering.md](./order-numbering.md) | Hand out next order number per country/year |
| InvoiceNumbering | [invoice-numbering.md](./invoice-numbering.md) | Hand out next gap-free invoice number per country/year |
| Outbox | [outbox.md](./outbox.md) | System-of-record for off-request-path work |
| Clock | [clock.md](./clock.md) | Testable "now" provider |
| IdGenerator | [id-generator.md](./id-generator.md) | Hand out fresh entity ids (ULID) |
| ManualOrderTransitionPolicy | [manual-order-transition-policy.md](./manual-order-transition-policy.md) | Strict allow-list for the admin manual state-change escape hatch |

## Application services

| Role | File | Responsibility |
|---|---|---|
| AuthService | [auth-service.md](./auth-service.md) | Orchestrate registration, login, refresh, OAuth, magic link, reset |
| UserSessionProvider | [user-session-provider.md](./user-session-provider.md) | Read-side wrapper over the current authenticated user's claims |

## Adapters

| Role | File | Responsibility |
|---|---|---|
| PaymentProvider | [payment-provider.md](./payment-provider.md) | Initiate / verify payments; webhook parsing |
| ShippingCarrier | [shipping-carrier.md](./shipping-carrier.md) | Shipments, labels, status |
| CompanyRegistry | [company-registry.md](./company-registry.md) | Look up businesses by registration number |
| EmailProvider | [email-provider.md](./email-provider.md) | Render template + submit to mail service |
| AddressGeocoder | [address-geocoder.md](./address-geocoder.md) | Autocomplete + geocode addresses |
| BlobStorage | [blob-storage.md](./blob-storage.md) | Store and stream files |

## Repository interfaces

Repository interfaces are listed as supporting roles but do not get full role files (their responsibility is "persist aggregate X" by definition). The aggregate's role file documents the methods callers need.

- `IOrderRepository`, `IMakerRepository`, `IProductRepository`, `IInvoiceRepository`, `IPayoutBatchRepository`, `IUserRepository`, `ICategoryRepository`, `IReviewRepository`, `IOrderMessageRepository`, `IDisputeRepository`, `ICountryConfigurationRepository`, `IAdminAuditLogRepository`, `IAddressRepository`, `IRefreshTokenRepository`, `IOutboxRepository`, `INumberingSequenceRepository`.

## How to add a role

1. Copy `_template.md` to `<role-name>.md` (kebab-case).
2. Fill in responsibility (one sentence!), collaborators, knows, does-not-know, lifecycle.
3. Add the row to the appropriate table above.
4. Link the role from any ADR or user story that introduces or extends it.
