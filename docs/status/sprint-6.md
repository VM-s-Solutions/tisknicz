# Sprint 6 — status

**Period:** 2026-05-28 → ongoing
**Goal (per `INDEX.md`):** Phase 3 (Catalog) end-to-end — backend read-side + customer-facing pages.

## Phase 3 backend — DONE

| Ticket | State | Commit | Notes |
|---|---|---|---|
| T-0040 | done | `99fe265` | Category entity + 6-category seed + `maker_categories` join + admin CRUD |
| T-0041 | done | `6879cd8` | Product entity + CRUD commands + image upload |
| T-0042 | done | `2dfa624` + `18965cb` | `IBlobStorageClient` + AzureBlobStorageClient + per-container policy; Copilot review folded |
| T-0043 | done | (merged) | `GetPagedMakers` query + Maker.Slug / catalog-stats fields + ix_makers_catalog_sort |
| T-0044 | done | (merged) | `GetMakerBySlug` query — header + active products + empty Reviews |
| T-0045 | done | (merged) | `GetProductById` query — product + images + owning-maker display info |
| T-0046b | done | (merged) | Public catalog `[ProducesResponseType]` annotations + canonical dev port 5104; typed NSwag regen |

All seven are on `master`. The public `CatalogController` (`/api/v1/catalog/makers`, `/makers/{slug}`, `/products/{productId}`) is the contract surface the Phase-3 frontend tickets light up.

## Phase 3 frontend — IN PROGRESS

Sequencing: **T-0046 → T-0047 → T-0048 → T-0049**. The customer flow `/katalog → /katalog/{slug} → /produkt/{id}` is built in the order callers appear, so each downstream page already has linkers when it ships. T-0049 (maker dashboard CRUD UI) is independent and slots in after T-0048 — it's a separate audience and doesn't unblock the public flow.

| Ticket | State | Owner | Notes |
|---|---|---|---|
| T-0046 | done | (merged) | `/katalog` list + filters + URL-state pagination. Triggered first NSwag regen of `public-api.v1.ts`. |
| T-0047 | done | frontend | `/katalog/[slug]` profile page merged (`54ffcd2`). Full ticket at `docs/tickets/T-0047-frontend-maker-profile.md`. Added `getMakerBySlug` + `buildProductImageUrl` + `RATING_BP_PER_STAR` to the catalog helper; added `lib/money/formatter.ts` (`formatCzk(amountMinor, currency)`); established `<section>`-not-`<main>` and plural-neutral i18n conventions. |
| T-0048 | **ready** | frontend | `/produkt/[productId]` product detail. Full ticket at `docs/tickets/T-0048-frontend-product-detail.md`. Extends `catalog.ts` with `ProductDetail` + `getProductById`; one Client Component (`product-gallery.tsx`) for thumbnail swap; reuses `buildProductImageUrl` + `formatCzk` from T-0047. |
| T-0049 | draft | — | `/dashboard/maker/produkty` CRUD UI + image picker. Will be expanded in parallel with T-0048 since it shares image-handling primitives with T-0041. |

## Open blockers

None. All four frontend tickets' dependencies are `done`.

## Carried follow-ups picked into Sprint 6

- (none specific to Phase 3 yet — Sprint 5 carry-overs continue to track on their per-ticket review trails)

## Definition of done (sprint level)

- [x] Phase 3 backend merged
- [x] T-0046 merged
- [x] T-0047 merged
- [ ] T-0048 merged
- [ ] T-0049 merged
- [ ] Sprint 6 retrospective added to this file
