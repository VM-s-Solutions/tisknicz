---
id: T-0163
title: Maker-proposed categories with admin approval
status: draft
size: L
owner:
created: 2026-08-01
updated: 2026-08-01
depends_on: [T-0040, T-0041, T-0043, T-0119]
blocks: []
user_stories: [US-maker-0020, US-admin-0019]
adrs: [0004, 0011, 0013, 0014, 0020]
phase: 7
manual_steps: [nswag-regen, ef-migration]
security_touching: true
layers: [dotnet-db, dotnet-backend, frontend, l10n]
---

# T-0163 — Maker-proposed categories with admin approval

## Context

Operator directive (2026-08-01): *"Makers should be able to create new category. If
the category does not exist yet, admin has to approve adding of product in this
category and next maker should be able to use that category from that point."*

Today the category list is a closed set. `Category` is admin-only CRUD (T-0040,
US-admin-0013), the picker is data-driven from the anonymous
`GET /api/v1/catalog/categories` endpoint (T-0119), and `CreateProduct` hard-fails
with `category.notActive` for anything not already in the table. A maker whose
craft doesn't fit one of the six launch categories ("3D tisk", "Klasický tisk",
"Potisk textilu", "Laser & CNC", "Velkoformát", "Handmade") has no path forward
except emailing the operator — which is precisely the manual intervention the
platform is designed to avoid.

This ticket opens the taxonomy to makers **behind an admin gate**. A maker types a
new category name inline on the product form; the category is created in a
`Pending` state and the product is created against it but withheld from every
public surface. An admin approves (optionally correcting the name/slug), merges it
into an existing category, or rejects it with a reason. On approval the category
becomes selectable for **every** maker and the withheld products publish
automatically.

The admin gate is not optional polish — the proposed name becomes a public,
SEO-indexed URL segment and a public filter chip. Unmoderated maker free text
reaching those surfaces is the security concern this ticket is `security_touching`
for.

## Scope

### Domain (`Core.Domain/Categories/`)

- **New `CategoryStatus` enum** — `Approved = 1`, `Pending = 2`, `Rejected = 3`.
  Approved is `1` so the migration backfills every existing row to it with a
  column default and no data script.
- **`Category` gains** `Status`, `ProposedByMakerId` (null for admin-created),
  `ReviewNote` (admin's rejection reason / merge note), `MergedIntoCategoryId`.
- **New factory `Category.Propose(id, name, countryCode, proposedByMakerId)`** —
  slug derived from the name (never caller-supplied on this path; makers don't get
  slug control), `Status = Pending`, `SortOrder = ProposedSortOrder` (a large
  sentinel; the admin assigns a real one on approval).
- **New transitions**, each guarding `Status == Pending`:
  - `Approve(name, slug, icon, description, sortOrder)` — admin may correct all
    five. The slug **is** editable here (unlike `UpdateMetadata`, which freezes it
    per US-admin-0013 AC-2) because a pending slug has never been public, so there
    is no SEO link to break. This is the only legitimate slug edit in the system.
  - `Reject(reviewNote)` — `Status = Rejected` **and** `MarkDeactivated(...)`.
  - `MergeInto(targetCategoryId, reviewNote)` — `Status = Rejected`,
    `MergedIntoCategoryId = target`, `MarkDeactivated(...)`.
- **`Product.ReassignCategory(categoryId)`** — new mutator, needed only by the
  merge path. (`MakerId` remains non-reassignable; only the category moves.)

### DB (`Infra.Database/`)

- EF migration `AddCategoryProposals`:
  - `categories.status` `TEXT NOT NULL DEFAULT 'Approved'` (`HasConversion<string>()`,
    matching `Product.PriceType` / `Order` / `EmailTemplate` convention — readable
    in raw SQL).
  - `categories.proposed_by_maker_id` (nullable, FK → `makers.id`, `ON DELETE SET NULL`),
    `categories.review_note` (nullable, max 500), `categories.merged_into_category_id`
    (nullable, self-FK, no cascade).
  - Index `ix_categories_status_pending` — partial on `status = 'Pending'`; the
    admin queue and the dashboard count tile are the only readers and both filter
    on exactly that.
  - **The existing `ix_categories_slug` partial unique index (`HasFilter("is_active")`)
    is left untouched** and does load-bearing work here — see Technical notes.
- Seed migration for the two new email templates (`cs-CZ` rows), following the
  T-0067 template-seed shape.

### AppServices — maker propose path

- **`CreateProduct.Command` gains `string? ProposedCategoryName`.** Exactly one of
  `CategoryId` / `ProposedCategoryName` must be non-empty (`category.eitherIdOrProposal`
  otherwise). One round-trip, one UoW: the category and the product commit
  atomically, so an abandoned form never leaves an orphan pending row.
- Handler resolution order when `ProposedCategoryName` is supplied:
  1. Slugify the name; look up by slug **ignoring the soft-delete filter**.
  2. Hit is `Approved` + active → **no proposal is created**; the product is
     attached to the existing category and goes live immediately. (A maker typing
     "3d tisk" gets the seeded category, not a duplicate.)
  3. Hit is `Pending` → **reuse that row**; the second maker's product joins the
     first maker's proposal in the same review item.
  4. Hit is `Rejected` (or otherwise inactive) → refuse with
     `category.proposal.nameTaken`. Makers cannot resurrect a name an admin
     already declined.
  5. No hit → `Category.Propose(...)`, enqueue the admin-notification outbox event.
- **Per-maker cap**: at most `MaxPendingProposalsPerMaker = 3` open proposals,
  else `category.proposal.limitReached`.
- Name screened through the existing `ProhibitedContent.ContainsProhibitedTerm`
  (reuses `category.nameNotAllowed`), same as the admin `CreateCategory.Validator`.

### AppServices — admin review path

Three explicit commands rather than one overloaded verb, so the audit action codes
read cleanly. All three implement `IAdminAuditableCommand` (ADR 0014):

| Command | Action code | Effect |
|---|---|---|
| `ApproveCategoryProposal` | `category.proposal.approve` | `Status = Approved`; withheld products publish automatically |
| `RejectCategoryProposal` | `category.proposal.reject` | `Status = Rejected` + deactivated; products stay withheld |
| `MergeCategoryProposal` | `category.proposal.merge` | Products reassigned to the target and publish; proposal rejected + deactivated |

Plus two reads: `GetCategoryProposals` (queue: proposed name, proposing maker,
affected product count, created-at) and `GetPendingCategoryProposalsCount` (dashboard
tile, mirroring `GetProcessingPayoutsCount` / `GetStalledOutboxCount`).
`GetAdminCategories.AdminCategoryItem` gains `Status`.

### Public visibility gate

Product visibility is **derived from the category's status**, not denormalised onto
the product — see Alternatives Considered. Every anonymous product read joins
`Category` and requires `Status == Approved`:

- `CatalogQueries.GetMakerBySlugAsync` — the maker-profile product list.
- `CatalogQueries.GetProductByIdAsync` — product detail; a withheld product returns
  `null` → 404, so it is not probeable by id.
- `MakerProductQueries` (the maker's own dashboard) does **not** gate — it instead
  projects `CategoryStatus` + `ReviewNote` so the maker sees the pending/rejected
  badge and the admin's reason.
- `CreateOrder` refuses a product whose category is not `Approved`
  (`product.categoryPending`) — defence in depth; the checkout page is already
  unreachable because detail 404s.
- `GetPublicCategories` / the maker picker already call `GetActiveAsync`, which
  gains the `Status == Approved` predicate — so a pending name never appears in a
  filter chip or in another maker's dropdown.

### Web hosts

- `Web.Admin/Controllers/CategoriesController` — `POST {id}/approve`,
  `POST {id}/reject`, `POST {id}/merge`, `GET proposals`. Existing `[Authorize]`
  under the admin audience (ADR 0013).
- `Web.Maker` product-create endpoint carries the new optional field; no new route.

### Outbox + email (ADR 0020)

- `category.proposal.submitted.adminEmail` → `CategoryProposalSubmittedAdmin`.
- `category.proposal.reviewed.makerEmail` → **two** templates,
  `CategoryProposalApprovedMaker` / `CategoryProposalRejectedMaker`. A **merge**
  sends the *approved* template with the target category's name in the payload
  ("Váš produkt je nyní v kategorii {name}") — from the maker's point of view a
  merge is an approval that landed somewhere else.

### Frontend

- `product-form.tsx` — the category `<select>` gains a trailing **"Jiná kategorie…"**
  option that reveals a text input; submitting sends `proposedCategoryName` instead
  of `categoryId`. Inline hint: the product will be published once an admin approves.
- Maker product list + detail — `Čeká na schválení` / `Kategorie zamítnuta` badge
  with the admin's `reviewNote`, and a nudge to pick an existing category.
- `app/(admin)/dashboard/admin/kategorie/` — a **Návrhy kategorií** section above
  the existing list: proposed name, maker, product count, and Approve (with
  editable name/slug/sort order) / Merge (target picker) / Reject (reason,
  required) actions.
- `app/(admin)/dashboard/admin/page.tsx` — pending-proposals count tile.
- `lib/i18n/cs-CZ.ts` — a key per new `BusinessErrorMessage` code plus the UI copy
  above. Vykání is not in play here (maker surface → tykání, per CLAUDE.md).
- NSwag regen of the affected generated clients in the same PR (admin + maker
  specs both change).

### New error codes (`BusinessErrorMessage`)

`category.eitherIdOrProposal`, `category.proposal.nameTaken`,
`category.proposal.limitReached`, `category.proposal.notPending`,
`category.proposal.mergeTargetInvalid`, `product.categoryPending`.

## Alternatives considered

- **Overload `IsActive` as the pending flag instead of adding `Status`** —
  *rejected*: `IsActive` is soft-delete and is load-bearing in two places (the
  global query filter and the `ix_categories_slug` partial unique index). A pending
  row would be indistinguishable from an admin-deactivated one, and because the
  unique index only covers `is_active` rows, an inactive-pending row would free its
  slug for a second maker to claim — producing exactly the duplicate proposals this
  design is trying to collapse. The two concerns stay orthogonal.
- **Denormalise a `ProductStatus` onto `products` and flip it on approval** —
  *rejected*: it duplicates a fact the category already owns, needs a bulk `UPDATE`
  inside the approve transaction (unbounded row count for a popular proposal), and
  introduces a drift class where a product is `Published` under a category that was
  later rejected. Deriving visibility from the join keeps one source of truth and
  makes approval instantly publish every affected product with zero writes to
  `products`. Cost is one PK join on the catalog read path against a table of a few
  dozen rows — the optimizer should confirm, but it is not a plausible regression.
- **A standalone `CategoryProposal` entity separate from `Category`** —
  *rejected*: approval would then have to *create* a `Category` and rewrite every
  product's FK, which is the merge path's expensive write applied to the common
  case. Proposing into the same table means approval is a single status flip. The
  cost — proposals occupy slug uniqueness before approval — is a feature here
  (it is what makes step 3 above collapse duplicates).
- **Block product creation until the category is approved** (maker submits a
  category request first, comes back later) — *rejected by the operator* in
  favour of "product saved, hidden until approved": the maker finishes their work
  in one sitting and the product publishes itself when the admin gets to it.
- **Approve/reject only, no merge** — *rejected by the operator*: makers will
  propose "Resin", "Pryskyřice" and "3D resin" for the same thing. Without merge,
  the admin either accepts a fragmented taxonomy or rejects proposals and strands
  the products.
- **Let makers supply the slug** — *rejected*: the slug is a public URL segment;
  deriving it from the (admin-reviewed) name keeps one moderated string instead of
  two.

## Out of scope

- **Proposing a category from `UpdateProduct`.** Proposal is a create-time action
  only. A maker whose proposal was rejected re-points the existing product to an
  approved category through the normal `UpdateProduct` path (`CategoryId` is already
  in that command) — nothing is stranded.
- **Category hierarchy / subcategories.** The taxonomy stays flat.
- **Maker-initiated edit or withdrawal of a submitted proposal.** Once submitted it
  is the admin's queue item.
- **Auto-approval heuristics** (trusted-maker tiers, fuzzy duplicate detection
  beyond exact slug match). Every proposal is reviewed by a human at MVP.
- **Retroactive re-categorisation of already-published products** when a merge
  target changes later.
- **`MakerCategory`** (the maker↔category join driving the catalog filter) is not
  touched — proposals attach a *product* to a category, not a maker.

## Acceptance criteria

- **AC-1** Given a maker on the product form, when they choose "Jiná kategorie…",
  type a name that matches no existing category, and submit, then a `Category` is
  persisted with `Status = Pending`, `ProposedByMakerId` = their maker id, and a
  slug derived from the name; the `Product` is persisted against it in the **same**
  transaction; the response is 200 with the new product id.
- **AC-2** Given a product whose category is `Pending`, when an anonymous visitor
  loads the maker's public profile or requests the product detail by id, then the
  product does not appear in the profile's product list and the detail request
  returns 404 — the product is not probeable by id.
- **AC-3** Given the same product, when the owning maker opens their dashboard, then
  it is listed with a "čeká na schválení" badge naming the proposed category, and
  editing it via `UpdateProduct` still works.
- **AC-4** Given maker A has a pending proposal "Pryskyřice", when maker B submits a
  product with a proposed name that slugifies to the same value, then **no second
  category row is created** — B's product attaches to A's pending category and both
  wait on one review item.
- **AC-5** Given a maker types a name that slugifies to an existing **approved,
  active** category, when they submit, then no proposal is created, the product is
  attached to that existing category, and it is publicly visible immediately.
- **AC-6** Given a maker types a name that slugifies to a previously **rejected**
  category, when they submit, then the command fails with
  `category.proposal.nameTaken` and nothing is persisted.
- **AC-7** Given an admin approves a pending proposal, when the command succeeds,
  then `Status = Approved`; every product in that category becomes publicly visible
  with no further action; the category appears in `GET /catalog/categories` and
  therefore in the public filter list and in **every** maker's product-form picker.
- **AC-8** Given an admin approves while correcting the name, slug, icon,
  description and sort order, then all six are persisted; a corrected slug colliding
  with an existing active category is refused with `category.slugAlreadyExists` and
  nothing is persisted.
- **AC-9** Given an admin merges a proposal into an existing approved category, when
  the command succeeds, then every product in the proposal is reassigned to the
  target category and becomes publicly visible; the proposal row is
  `Status = Rejected`, deactivated, with `MergedIntoCategoryId` set; the maker
  receives the approved-style email naming the **target** category.
- **AC-10** Given an admin merges into a target that does not exist, is not
  `Approved`, is inactive, or is the proposal itself, then
  `category.proposal.mergeTargetInvalid` and nothing is persisted.
- **AC-11** Given an admin rejects a proposal with a reason, then `Status = Rejected`
  and the row is deactivated; affected products remain hidden from every public
  surface; the owning maker sees the reason on their product and can re-point the
  product to an approved category via `UpdateProduct`, after which it publishes
  normally.
- **AC-12** Given approve / reject / merge is called against a category whose status
  is not `Pending`, then `category.proposal.notPending` and nothing is persisted.
- **AC-13** Given a proposed name containing a `ProhibitedContent` term, when
  submitted, then `category.nameNotAllowed` — no category and **no product** are
  persisted.
- **AC-14** Given a maker already has 3 pending proposals, when they submit a
  fourth, then `category.proposal.limitReached` and nothing is persisted.
- **AC-15** Given a pending proposal exists, then its name appears on **no**
  anonymous surface — not in `GET /catalog/categories`, not in the catalog filter
  chips, not on any product page, not in the sitemap. It is visible only to the
  proposing maker and to admins.
- **AC-16** Given a customer somehow reaches order creation for a product whose
  category is not `Approved`, when `CreateOrder` runs, then it is refused with
  `product.categoryPending` and no order is persisted.
- **AC-17** Given approve / reject / merge is called without a resolvable admin
  session, then `401 auth.required` and nothing is persisted (fail-closed, per the
  `CreateCategory` / `RefundOrder` precedent).
- **AC-18** Given any of the three review commands succeeds, then an
  `AdminAuditLogEntry` captures before/after JSONB with the matching action code
  (`category.proposal.approve|reject|merge`).
- **AC-19** Given the migration runs against existing data, then every existing
  category row is `Status = 'Approved'`, every existing product stays publicly
  visible, and the public catalog behaves exactly as before.
- **AC-20** Given `CreateProduct` receives both `CategoryId` and
  `ProposedCategoryName`, or neither, then `category.eitherIdOrProposal` and
  nothing is persisted.
- **AC-21** Given pending proposals exist, when an admin loads the dashboard, then a
  count tile shows the pending total and links to the review queue.

## Technical notes

- **The slug index is the dedupe mechanism.** `ix_categories_slug` is
  `UNIQUE … WHERE is_active`. Pending rows are `IsActive = true` (they are not
  deleted), so they occupy the index and a concurrent duplicate proposal loses at
  the DB level — the `UniqueConstraintTranslator` already maps that constraint to
  `category.slugAlreadyExists`. The AC-4 reuse path is the *happy* resolution of
  the same race; the translator is the TOCTOU backstop. Rejected rows are
  deactivated and therefore free their slug, which is deliberate: it lets an admin
  later create the category properly on their own terms. AC-6 is what stops a
  *maker* from walking through that same door.
- **`IgnoreQueryFilters()` is query-wide in EF Core.** `MakerProductQueries` needs
  the category row even when it is rejected-and-deactivated, but slapping
  `IgnoreQueryFilters()` on the joined query would also unfilter `Product` and leak
  the maker's own soft-deleted products into their dashboard. Resolve the category
  state as a **separate** `IgnoreQueryFilters()` lookup keyed by the distinct
  category ids of the page, or re-assert `p.IsActive` explicitly. Cover this with a
  test — it is a silent data leak if missed.
- **The merge write is unbounded.** `MergeCategoryProposal` loads every product in
  the proposal via a new `IProductRepository.GetByCategoryIdAsync`. At MVP volumes
  a tracked load is fine; if a proposal ever accumulates hundreds of products,
  revisit with `ExecuteUpdateAsync` — but note that bypasses the audit interceptor,
  so it is not a free swap.
- **`Category.Approve` is the sole slug-mutating path.** `UpdateMetadata` must stay
  slug-frozen (US-admin-0013 AC-2). Keep them as separate methods; do not
  generalise.
- **Query-filter interaction on the catalog join.** `Category` has the global
  soft-delete filter, so joining it into `CatalogQueries` already excludes
  deactivated categories — the added predicate is only `Status == Approved`.
  Verify the generated SQL for the maker-profile query does not regress into a
  correlated subquery per product.
- **Enum storage.** `HasConversion<string>()` + `HasDefaultValue`, matching
  `Product.FulfillmentType` (T-0144), so `dotnet ef database update` and raw SQL
  both read cleanly and existing rows backfill without a data script.
- **SQLite portability.** The test harness runs SQLite; avoid `EF.Functions.ILike`
  and `ORDER BY` on `DateTimeOffset` in the new queries (order proposals by `Id` —
  ids are time-ordered ULIDs), per the existing `CatalogQueries` comments.
- **Local dev.** Email sending is stubbed locally, so verify the two new templates
  by asserting the outbox rows, not by expecting mail.

## Files touched (expected)

**Backend — domain**
- `backend/src/Makables.Core.Domain/Categories/CategoryStatus.cs` *(new)*
- `backend/src/Makables.Core.Domain/Categories/Category.cs`
- `backend/src/Makables.Core.Domain/Categories/ICategoryRepository.cs`
- `backend/src/Makables.Core.Domain/Categories/ICategoryQueries.cs`
- `backend/src/Makables.Core.Domain/Products/Product.cs`
- `backend/src/Makables.Core.Domain/Products/IProductRepository.cs`
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs`
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs`
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs`

**Backend — app services**
- `backend/src/Makables.Core.AppServices/Features/Products/CreateProduct.cs`
- `backend/src/Makables.Core.AppServices/Features/Categories/ApproveCategoryProposal.cs` *(new)*
- `backend/src/Makables.Core.AppServices/Features/Categories/RejectCategoryProposal.cs` *(new)*
- `backend/src/Makables.Core.AppServices/Features/Categories/MergeCategoryProposal.cs` *(new)*
- `backend/src/Makables.Core.AppServices/Features/Categories/GetCategoryProposals.cs` *(new)*
- `backend/src/Makables.Core.AppServices/Features/Categories/GetPendingCategoryProposalsCount.cs` *(new)*
- `backend/src/Makables.Core.AppServices/Features/Categories/GetAdminCategories.cs`
- `backend/src/Makables.Core.AppServices/Features/Catalog/GetPublicCategories.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/CreateOrder.cs`

**Backend — infra + hosts**
- `backend/src/Makables.Infra.Database/Configurations/CategoryConfiguration.cs`
- `backend/src/Makables.Infra.Database/Categories/CategoryRepository.cs`
- `backend/src/Makables.Infra.Database/Categories/CategoryQueries.cs`
- `backend/src/Makables.Infra.Database/Products/ProductRepository.cs`
- `backend/src/Makables.Infra.Database/Products/MakerProductQueries.cs`
- `backend/src/Makables.Infra.Database/Catalog/CatalogQueries.cs`
- `backend/src/Makables.Infra.Database/Migrations/<ts>_AddCategoryProposals.cs` *(new)*
- `backend/src/Makables.Infra.Database/Migrations/<ts>_SeedCategoryProposalEmailTemplates.cs` *(new)*
- `backend/src/Makables.Web.Admin/Controllers/CategoriesController.cs`
- `backend/src/Makables.Web.Maker/Controllers/ProductController.cs`

**Frontend**
- `frontend/src/lib/api-client/` *(NSwag regen — not hand-edited)*
- `frontend/src/lib/api-client-helpers/admin-categories.ts`
- `frontend/src/lib/catalog/load-category-options.ts`
- `frontend/src/app/(maker)/dashboard/maker/produkty/_components/product-form.tsx`
- `frontend/src/app/(maker)/dashboard/maker/produkty/product-card.tsx`
- `frontend/src/app/(admin)/dashboard/admin/kategorie/page.tsx`
- `frontend/src/app/(admin)/dashboard/admin/kategorie/proposal-row.tsx` *(new)*
- `frontend/src/app/(admin)/dashboard/admin/page.tsx`
- `frontend/src/lib/i18n/cs-CZ.ts`

**Tests**
- `backend/src/Makables.Tests/Domain/Categories/CategoryTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Categories/ApproveCategoryProposalHandlerTests.cs` *(new)*
- `backend/src/Makables.Tests/AppServices/Features/Categories/RejectCategoryProposalHandlerTests.cs` *(new)*
- `backend/src/Makables.Tests/AppServices/Features/Categories/MergeCategoryProposalHandlerTests.cs` *(new)*
- `backend/src/Makables.Tests/AppServices/Features/Products/CreateProductHandlerTests.cs`

## Suggested split

The ticket is `L`. If it needs to ship incrementally, the seam is after the
approve path — merge and reject are independently deployable on top of a working
propose→approve loop:

1. **T-0163a** — domain + migration + propose path + visibility gate + approve.
   AC-1..AC-8, AC-12..AC-17, AC-19, AC-20.
2. **T-0163b** — reject + merge + maker-facing reason + emails. AC-9..AC-11, AC-18.
3. **T-0163c** — admin queue UI polish + dashboard count tile. AC-21.

Splitting is optional; 1 must not ship without the AC-15 gate.

## Test plan reference

`docs/test-plans/T-0163.md` *(to be written by QA)*

## Status log
- 2026-08-01 `draft` created by Claude from operator directive
