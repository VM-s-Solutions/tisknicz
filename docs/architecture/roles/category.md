---
role: Category
kind: aggregate
status: accepted
---

# Category

## Responsibility

Be the reference data for product categorization (3D tisk, Klasický tisk, Potisk textilu, Laser & CNC, Velkoformát, Handmade).

## Collaborators

- **Product** (many-to-one)
- **Maker** (many-to-many via maker-category, denoting which categories a maker offers)

## Knows

- `Name`, `Slug`, `Icon`, `Description`, `SortOrder`
- `IsActive` (admin can hide a category from new products without deleting)

## Does NOT know

- The products inside it (reverse-queried)
- Maker availability

## Lifecycle

- **Created by:** seed migration (6 launch categories); admin-managed thereafter (audited)
- **Modified by:** `UpdateCategory.Command` (admin, audited)
- **Persisted by:** `ICategoryRepository`
- **Destroyed by:** soft delete only

## Implementation pointer

`backend/src/Makables.Core.Domain/Categories/Category.cs`.

## Related

- Roles: `maker`, `product`
- Seed data lives in `Makables.Infra.Database/Migrations/<initial>.cs` per ADR 0004
