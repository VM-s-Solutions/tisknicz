---
role: Product
kind: aggregate
status: accepted
---

# Product

## Responsibility

Represent a catalog entry a maker offers for sale, with the pricing, media, and shipping metadata needed for the catalog page and order creation.

## Collaborators

- **Maker** (parent; product cannot exist without one)
- **Category** (many-to-one)
- **BlobStorage** (asks: store and serve images)
- **Money** (uses: price representation)

## Knows

- Title, description
- Price (Money: `base_price_minor + currency`) and `PriceType` (`Fixed | From | OnRequest`)
- `FulfillmentType` (`MadeToOrder | InStock`, default `MadeToOrder`) — "na zakázku" vs. "skladem" (T-0144). Drives the product-detail badge and the checkout withdrawal-right notice (§ 1837 písm. d) OZ exempts made-to-order goods from the standard 14-day right of withdrawal; in-stock goods carry the normal 14-day right). Maker-set, independent of `PriceType` (pricing certainty vs. production timing are orthogonal — see T-0144 Alternatives Considered).
- Category
- Images (list of blob paths)
- Weight (grams) for Zásilkovna
- `IsActive` (maker can hide without deleting)

## Does NOT know

- Order history involving it (queries the Order side)
- Stock quantity / inventory count (out of scope at MVP — `FulfillmentType` only drives the legal notice, not stock-count tracking)
- Price history (we don't track it; the order carries the price snapshot from order time)
- Whether the maker is verified or active (catalog query joins on Maker)

## Lifecycle

- **Created by:** `CreateProduct.Command` (maker action)
- **Modified by:** `UpdateProduct.Command` (maker action)
- **Persisted by:** `IProductRepository`
- **Destroyed by:** soft delete only via `DeleteProduct.Command` (maker action; sets `IsActive = false`)

## Invariants

- `Price.AmountMinor >= 0`. Free products allowed only if `PriceType = OnRequest`.
- `Currency` matches the maker's `Country.DefaultCurrencyCode` from `CountryConfiguration`.
- A product belongs to exactly one maker; reassignment is not supported (delete + recreate).
- `PriceType = OnRequest` ⇒ price field is informational only; orders aren't placeable until custom-quote flow ships (post-MVP).
- Up to N images per product (N defined in config; default 10).
- `FulfillmentType` enum membership only (no cross-field invariant with `PriceType`); the migration column default is `MadeToOrder` so every pre-existing row backfills to the safer legal posture with no manual data fix (T-0144 AC-6). Not snapshotted on `Order` — the checkout notice resolves it from the product at render time.

## Implementation pointer

`backend/src/Makables.Core.Domain/Products/Product.cs`.

## Related

- ADRs: 0003, 0010 (Address: pickup info on Maker), 0011 (blob storage), 0023 (NFR — image dimensions)
- Stories: product CRUD, catalog browse
- Roles: `maker`, `category`, `money`, `blob-storage`
