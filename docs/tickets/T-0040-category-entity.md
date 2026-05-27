---
id: T-0040
title: Category entity + ICategoryRepository + seed (6 launch categories) + maker_category join + admin CRUD
status: done
size: M
owner: dotnet-backend
created: 2026-05-27
updated: 2026-05-27
depends_on: [T-0010]
blocks: [T-0041, T-0043]
adrs: [0014]
phase: 3
---

# T-0040 — Category entity + admin CRUD

## Scope

The Phase-3 catalog read/write surface starts here. Category is reference data: seeded with the 6 launch categories per `docs/architecture/roles/category.md`, admin-managed thereafter (US-admin-0013).

### Domain (`Core.Domain/Categories/`)
- `Category.cs` (`Auditable`) — `Name`, `Slug`, `Icon?`, `Description?`, `SortOrder`. Carries `Slugify(...)` (NFD decompose → drop combining marks → lowercase → collapse non-alphanumerics to `-` → trim leading/trailing dashes). `Create` accepts an admin-supplied slug override; otherwise derives from name. `UpdateMetadata` renames + re-orders but does NOT touch `Slug` (US-admin-0013 AC-2 — URL segments must survive renames).
- `ICategoryRepository.cs` — `Add`, `GetByIdAsync` (tracked), `SlugExistsAsync` (slug-uniqueness pre-check).

### Core.Domain.Common
- `BusinessErrorMessage.{CategoryNotFound, CategoryNotActive, CategorySlugAlreadyExists}` added.

### Core.AppServices (`Features/Categories/`)
- `CreateCategory.cs` — admin command. Pre-allocates the `Id` in the command shape so the `AdminAuditPipelineBehavior` has a stable `TargetId` (the before-snapshot lookup returns null pre-handler, which is the expected Create shape). Resolves slug (admin override or `Slugify(name)`), pre-checks `SlugExistsAsync`, then `categories.Add(...)`. Returns `Response(Id, Slug)`.
- `UpdateCategory.cs` — admin rename + re-order. `Slug` deliberately NOT in the command shape.
- `DeactivateCategory.cs` — soft-delete. Same shape as `DeactivateMaker` (T-0034 sec reviewer m-1 fail-closed pattern).
- All three implement `IAdminAuditableCommand` with action codes `category.{create,update,deactivate}` and document the host-level `[Authorize(Roles="Admin")]` requirement in XML doc.

### Infra.Database
- `Configurations/CategoryConfiguration.cs` — `categories` table. Partial unique index `ix_categories_slug` `WHERE is_active` so soft-deleted rows free the slug for reuse.
- `Categories/CategoryRepository.cs` — EF impl with tracked reads.
- `UniqueConstraintTranslator.cs` — extended with `ix_categories_slug` → `CategorySlugAlreadyExists` (Conflict). Pre-check + race-translation belt-and-braces shape, same as T-0033 makers.
- `Migrations/20260527211229_Categories.cs` — creates `categories` table, `maker_categories` join (composite PK on `(maker_id, category_id)`, audit columns; no domain entity — pure m:n reference data), and seeds the 6 launch categories via raw SQL with deterministic `created_at = 2026-05-27` and `created_by = 'seed'` (matches the `CountryConfiguration` seed pattern from T-0010).

### Seed shape (6 launch categories)
| id | name | slug | sort_order |
|---|---|---|---|
| `cat-3d-tisk` | 3D tisk | 3d-tisk | 10 |
| `cat-klasicky-tisk` | Klasický tisk | klasicky-tisk | 20 |
| `cat-potisk-textilu` | Potisk textilu | potisk-textilu | 30 |
| `cat-laser-cnc` | Laser & CNC | laser-cnc | 40 |
| `cat-velkoformat` | Velkoformát | velkoformat | 50 |
| `cat-handmade` | Handmade | handmade | 60 |

### DI
- `AddMakablesInfrastructure` registers `ICategoryRepository → CategoryRepository` (scoped).

### Out of scope
- HTTP controllers — admin frontend (T-0119) wires `/api/v1/admin/categories/*`.
- Maker-categories management on the Maker profile (`UpdateMakerProfile` doesn't touch the join yet — deferred per T-0034 scope reduction).
- Catalog query that consumes the join (`GetPagedMakers` with category filter is T-0043).
- Public read query (`GET /api/v1/categories`) — lands with the catalog frontend (T-0046).

### Tests (+40 facts; 733 total = 651 unit + 82 integration)
- `Domain/Categories/CategoryTests.cs` — 17 facts including the Slugify theory matrix (6 canonical names + diacritic-heavy "Žluťoučký KŮŇ" + spaces + edge cases).
- `AppServices/Features/Categories/CreateCategoryHandlerTests.cs` — 6 facts.
- `AppServices/Features/Categories/UpdateCategoryHandlerTests.cs` — 4 facts.
- `AppServices/Features/Categories/DeactivateCategoryHandlerTests.cs` — 4 facts.

## Acceptance criteria
- **AC-1** `Category.Create` derives slugs from name when not supplied; admin override accepted; rejects invalid slug shapes.
- **AC-2** `Slugify` strips Czech diacritics correctly (theory matrix pins all 6 launch names + a worst-case "žluťoučký kůň").
- **AC-3** `UpdateMetadata` renames + re-orders without touching `Slug` (US-admin-0013 AC-2 — URL stability).
- **AC-4** `CreateCategory` pre-checks `SlugExistsAsync`; TOCTOU race surfaces via `UniqueConstraintTranslator` → same `CategorySlugAlreadyExists` Conflict.
- **AC-5** All three admin commands fail-closed on missing session user (`Error.Unauthorized()`).
- **AC-6** Soft-deleted categories invisible to `GetByIdAsync` via the global query filter; partial unique index `ix_categories_slug WHERE is_active` lets a deactivated slug be reused.
- **AC-7** Migration seeds 6 launch categories with deterministic id + audit fields (`created_by = 'seed'`).
- **AC-8** `maker_categories` join table exists with composite PK + index on `category_id` (supports the catalog query's `WHERE category_id = ?` filter in T-0043).
- **AC-9** 733 tests pass (651 unit + 82 integration; +40 new).
- **AC-10** CLAUDE.md hygiene: no `SaveChangesAsync` in handlers; all error codes from `BusinessErrorMessage`; `Core.Domain` no third-party packages.

## Status log
- 2026-05-27 done. Build clean, 733 tests pass. Awaiting dual reviewer per workflow.
