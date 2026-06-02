# Sprint 6 — status

**Period:** 2026-05-28 → 2026-06-01 (closed)
**Goal (per `INDEX.md`):** Phase 3 (Catalog) end-to-end — backend read-side + customer-facing pages.
**Outcome:** Met. The public storefront flow `/katalog → /katalog/{slug} → /produkt/{id}` is live, the maker's first authoring surface (`/dashboard/maker/produkty`) is in production, and one new ADR (0024) was forced into existence by review feedback.

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

## Phase 3 frontend — DONE

Sequencing: **T-0046 → T-0047 → T-0048 → T-0049**. The customer flow `/katalog → /katalog/{slug} → /produkt/{id}` is built in the order callers appear, so each downstream page already has linkers when it ships. T-0049 (maker dashboard CRUD UI) is independent and slots in after T-0048 — it's a separate audience and doesn't unblock the public flow.

| Ticket | State | Owner | Notes |
|---|---|---|---|
| T-0046 | done | (merged) | `/katalog` list + filters + URL-state pagination. Triggered first NSwag regen of `public-api.v1.ts`. |
| T-0047 | done | frontend | `/katalog/[slug]` profile page merged (`54ffcd2`). Full ticket at `docs/tickets/T-0047-frontend-maker-profile.md`. Added `getMakerBySlug` + `buildProductImageUrl` + `RATING_BP_PER_STAR` to the catalog helper; added `lib/money/formatter.ts` (`formatCzk(amountMinor, currency)`); established `<section>`-not-`<main>` and plural-neutral i18n conventions. |
| T-0048 | done | frontend | `/produkt/[productId]` product detail merged. Full ticket at `docs/tickets/T-0048-frontend-product-detail.md`. Extended `catalog.ts` with `ProductDetail` + `getProductById`; one Client Component (`product-gallery.tsx`) for thumbnail swap; reuses `buildProductImageUrl` + `formatCzk` from T-0047. Promoted `truncateForMeta` to `lib/seo/`. |
| T-0049 | done | frontend | `/dashboard/maker/produkty` CRUD UI + image manager merged (`c76c276`). Full ticket at `docs/tickets/T-0049-frontend-maker-products.md`. L-sized — maker's first authoring surface. Depends on T-0049a/b backend prep (read queries + typed `maker-api.v1.ts`). Added helper module `lib/api-client-helpers/maker-products.ts` (multipart upload through `apiFetch`); separate audience from T-0046/47/48 (Maker host, not Public). Forced **ADR 0024** (SSR auth cookie forwarding) — first authenticated Server Component on the platform. |

## Open blockers

None. All four frontend tickets' dependencies are `done`.

## Carried follow-ups picked into Sprint 6

- (none specific to Phase 3 yet — Sprint 5 carry-overs continue to track on their per-ticket review trails)

## Definition of done (sprint level)

- [x] Phase 3 backend merged
- [x] T-0046 merged
- [x] T-0047 merged
- [x] T-0048 merged
- [x] T-0049 merged
- [x] Sprint 6 retrospective added to this file (see below)

---

## Retrospective

*Written 2026-06-01 from a workflow-mined synthesis of every Sprint 6 ticket status log, commit, and ADR. Numbers verified against `git log master --since="2026-05-28" --until="2026-06-02"` (36 sprint commits; 14 with T-0049 in the message). Where claims couldn't be substantiated against artefacts they were dropped.*

### What shipped

| Slice | Tickets | Artefacts on `master` |
|---|---|---|
| Phase 3 backend prep | T-0046b (Public response types + port 5104) | merged before sprint open |
| Public storefront | T-0046, T-0047, T-0048 | `/katalog`, `/katalog/[slug]`, `/produkt/[productId]` |
| Maker backend prep | T-0049a (read queries), T-0049b (typed responses + enum schema transformer) | one PR `feat/T-0049ab-maker-backend-prep` |
| Maker authoring | T-0049 (CRUD + image manager + delete modal) | `/dashboard/maker/produkty` |
| Architectural decision | ADR 0024 | `docs/adr/0024-ssr-auth-cookie-forwarding.md` |

The customer can browse and click through to a product detail page; the maker can create, edit, deactivate, and manage images on their own products. The maker's UI works under server-side rendering thanks to ADR 0024.

**Phase-3 backend smooth delivery.** The seven backend tickets (T-0040 Category, T-0041 Product, T-0042 Blob storage, T-0043 GetPagedMakers, T-0044 GetMakerBySlug, T-0045 GetProductById, T-0046b response types) merged first-pass with stable contracts and no rework once they landed on `master`. Three of the eight latent bugs surfaced this sprint (#5 `priceType` wire-contract; #6 schema-collision; and the 10× rating divisor that took T-0047 to catch) trace back to Phase-3-backend or earlier, but T-0040–T-0046b themselves shipped clean — every Phase-3 frontend ticket lit up on its expected schedule because the contract under it was solid. Worth not burying.

### Reusable primitives the sprint produced

Promoted from inline / ad-hoc into first-class:

- **`<section>` not `<main>` at route level** (T-0047) — locks the root layout's single `main` landmark.
- **URL-state pagination** (T-0046, extended T-0049) — `?page=N[&pageSize=M]`, NaN/<1 clamped to 1 in the Server Component, link builder only emits `pageSize` when non-default so canonical URLs stay clean.
- **`formatCzk(amountMinor, currency)`** + non-CZK fallback at the card boundary (T-0047) — formatter still asserts on direct calls; routes pre-guard with `'on_request'` copy. Dev/CI stays loud; the production route survives a bad row.
- **`RATING_BP_PER_STAR = 10_000`** (T-0047) — single source of truth, mirrors backend `CatalogQueries.BpPerStar`. Closed a 10× rendering bug across three call sites.
- **`buildProductImageUrl(blobPath)`** (T-0047) — `..`-rejecting, host-anchored URL builder used everywhere images appear.
- **`formatWeight`** (T-0048 → promoted to `lib/format/weight.ts` by T-0049) — Czech comma decimal via `Intl.NumberFormat('cs-CZ')`.
- **`truncateForMeta`** (T-0048 → promoted to `lib/seo/truncate-for-meta.ts`) — threshold scales with `max` (Round-4 fix).
- **`generateMetadata` branching** (T-0047) — only the `NotFound` title swaps; transient errors fall back to brand title so SEO doesn't see a transient blip as a missing entity.
- **Plural-neutral i18n** ("Label: N" shape) — workaround for missing `Intl.PluralRules('cs')`; flagged in the cs-CZ.ts comment so T-0048+ follows.
- **`AddMakablesOpenApi` enum schema transformer** (T-0049b) — platform-wide rewrite of `IsEnum` schemas from `integer` to a string union matching `JsonStringEnumConverter`'s runtime emission. Applied across all four hosts.
- **`[ProducesResponseType]` discipline** (T-0046b + T-0049b) — per-action typed 200, honest error codes (don't claim `400 → Error` on endpoints where model binding can emit `ValidationProblemDetails`).
- **`apiFetch` SSR cookie forwarding** (T-0049 + ADR 0024) — chokepoint detection of the server runtime, audience-scoped cookie pair read via `next/headers`. Pattern established; T-0049 proved it works once. Phase 4's first authenticated SSR pages will reveal the edge cases (token refresh across the boundary, multi-cookie scenarios, etc.).
- **`apiFetch` multipart support** (T-0049) — pass `body: FormData` without `Content-Type`; browser writes the boundary.
- **`ApiError.fields` flattening** (T-0049, Sprint-2-era latent bug) — `parseErrorResponse` now collapses both validation shapes (multi-field `details: ValidationDetail[]` AND single-field `Error.Validation(field, code)`) into `Record<string, readonly string[]>` of display copy. Unblocks future forms that need per-input errors; T-0049's product form is the only consumer today.

### Latent bugs surfaced (Sprint-1/2/3 era issues this sprint exposed)

T-0049's review trail in particular was a Sprint-2 archeology dig. Counted:

1. **`parseErrorResponse` never produced `fields`** (Sprint 2). The wrapper claimed to read `payload.fields` but the backend wire shape is `{ field, code, type, details }`. T-0049's form was the first surface to consume per-field errors and exposed the gap.
2. **`application/problem+json` content type ignored.** The wrapper's content-type guard matched only `application/json`. ASP.NET's RFC 7807 responses use `application/problem+json` — framework 400s (model binding) and the framework 404 would fall through to the text branch and lose `title`/`detail`. Affects every host.
3. **Single-field validation never flattened.** Even if the wrapper had read `fields`, `Error.Validation(field, code)` would have been missed because that shape ships `field`/`code` at the top level with `details: null`. T-0049's review forced both shapes to be handled.
4. **Rating divisor off by 10×** (T-0046 carry-over → caught in T-0047). Docs claimed `÷1000`, backend is 10 000 bp/star. Every rated maker rendered as 5 stars (clamped). Three call sites fixed via `RATING_BP_PER_STAR`.
5. **`priceType` wire-contract lie** (platform-wide). `JsonStringEnumConverter` emits enum names at runtime, but `Microsoft.AspNetCore.OpenApi` builds schemas from the type model and didn't see the converter → emitted `integer`. Generated client typed `priceType: number` while the runtime accepted strings. The runtime tolerated both forms (`JsonStringEnumConverter` is lenient), so the spec lied silently. Fixed via `AddMakablesOpenApi` enum schema transformer on every host.
6. **Schema-collision on nested `Response` types** (T-0049b). `CreateProduct.Response` and `AddProductImage.Response` collided in OpenAPI naming. Worked around with controller-level `CreateProductResponse` / `UploadProductImageResponse` wrappers. The CQRS nesting convention in `Core.AppServices` stayed intact.
7. **Case-sensitive Cookie header guard** (T-0049). The SSR-cookie guard checked `headers['Cookie']` only; a caller-supplied `cookie` (lowercase) would have shipped both and the server would have either rejected or silently merged. Switched to `Object.keys(headers).some(h => h.toLowerCase() === 'cookie')`.
8. **HTTP default message leaked into field errors** (T-0049 self-inflicted, caught next round). Round 1 substituted `defaultMessage(response.status)` into `message` BEFORE passing to the field collector — so a vanilla `Error.Validation(field, code)` with no backend message would render "Server je momentálně nedostupný..." under the form input. Round 4 fixed by passing raw `payload.message`.

### What the review process actually caught

16 commits with "Copilot review" in the title across the sprint (2 on T-0047, 2 on T-0048, 7 on T-0049, 2 on T-0049a/b, 3 cluster-wide on T-0041 / T-0043 / T-0046b). The frontend Copilot rounds were the bulk: T-0049 went seven rounds on a single PR. The dual-reviewer (security + code-quality) pass that runs against every PR caught the visible architectural issues — but the latent platform bugs above (#1, #2, #3, #5, #7, #8) only surfaced under Copilot's per-commit review at PR-edit time, often **iteratively across rounds**.

Specific patterns:

- **Round-N "fix" introducing Round-(N+1) regression.** Twice on T-0049: the `createdOn` Date typing "fix" in Round 1 was an illusion (`Readonly<IMakerProductListItem>` inherits the generated `Date` typing verbatim) — Round 6 caught it. The validation-`fields` Round-2 fix passed the substituted `message` instead of `payload.message` — Round 4 caught it. Lesson: *incremental review needs a final read-through against the actual artefacts*, not against the previous round's diff.
- **Spec drift after implementation lands.** T-0048 shipped correct code with five ticket-text drifts (gallery props, weight separator, two namespace references, error-type casing) that Round 2 had to catch. The implementation was right; the spec wasn't — which is a process gap, not just a quirk. Phase 4 owner action: before opening a PR, re-read the ticket's AC list against the *shipped* commit (not against the spec at ticket-expansion time) and reconcile drift on the same PR.
- **Multi-round reviews on backend prep that nominally "didn't change behavior".** T-0049ab was supposed to be mechanical (`[ProducesResponseType]` attributes). It triggered three Copilot rounds and surfaced the `priceType` wire-contract bug, the schema-collision, and the `FileParameter` template gap — three platform-wide fixes. Mechanical PRs aren't.

### Process learnings

- **Hand-written `apiFetch` helpers stayed the right call.** Every PR re-confirmed it: the generated NSwag client throws on every non-2xx (typed `ErrorDto` for documented errors, `ApiException` for the rest), which doesn't fit the `Result<T, ApiError>` flow. The hand-written wrapper convention is now load-bearing across `profile.ts` / `catalog.ts` / `maker-products.ts`.
- **ADRs can emerge from review feedback, not just up-front planning.** ADR 0024 wasn't in the sprint plan; it was forced into existence by T-0049's security review finding that authenticated SSR pages don't carry cookies. Worth documenting as a process pattern: when a review surfaces an architectural gap, an ADR is the right place to record the choice.
- **Mechanical PRs surface latent platform bugs.** T-0049ab was supposed to be 3 attribute lines + 1 NSwag regen. It surfaced the `priceType` wire-contract bug that's been wrong since Phase-1 hosts went up.
- **Multi-round review on the same PR caught real bugs — but also exposed earlier-sprint debt.** T-0049 went seven rounds; without them at least the `parseErrorResponse` `fields` gap and the `createdOn` Date type lie would have shipped. The other framing matters too though: a Sprint-2-era contract mismatch survived four sprints before T-0049 was the first surface to consume it. That's an architectural-test gap, not a process win. Phase 4 owner action: stand up a "latent platform issues" audit pass — read every shipped `parseErrorResponse`-like contract surface and write at least one test that exercises the wire shape, before Phase-4 forms start surfacing the next gap.

### Follow-ups queued during the sprint

- **T-0049c** — `IOperationFilter` rewriting the multipart schema so `IFormFile` lands as `{ file: binary }` with `required: true`, instead of either `FileParameter | undefined` (current) or the synthetic `Body` class that inlines the entire `IFormFile` interface (what `[FromForm, Required]` produced). Defensive empty-file check in the upload action still enforces the contract at runtime; the gap is client-ergonomic. **Queued in INDEX.md.**
- **Czech `Intl.PluralRules` in `t()`** — the "Label: N" workaround is acceptable launch UX but feels mechanical. A proper plural picker would let strings read naturally ("1 hodnocení" / "3 hodnocení" / "12 hodnocení"). Small ticket, deferred.
- **Toast primitive in `components/ui/`** — T-0049's form uses a transient flash for "Saved"; a real toast is a small component but a real ask. Defer until two callers want it.
- **FluentValidation nested-rules support in the field flattener** — current PascalCase→camelCase normaliser handles top-level fields only. `Description.MaxLength` would mangle to `description.MaxLength` and miss the form's state key. T-0049's validators are all top-level; defer until a nested rule appears.
- **Categories list endpoint** — T-0119 already exists as the placeholder; Phase 3 hard-coded the 6 launch slugs in `lib/catalog/categories.ts`. Wire it up when admin category CRUD ships.
- **Duplicate `verified` i18n key consolidation** — `catalog.card.verified` (T-0046) + `catalog.maker.verified` (T-0047). Informational; consolidate when convenient.
- **`patterns.md` catalogue gap** — eight of the 14 primitives this sprint produced (URL-state pagination, `buildProductImageUrl`, `formatWeight`, `<section>`-not-`<main>`, `generateMetadata` NotFound/transient branching, multipart through `apiFetch`, ADR-0024 SSR cookie forwarding, `RATING_BP_PER_STAR`-style shared constants) are load-bearing enough to deserve explicit subsections in `docs/architecture/patterns.md`, not just retro mentions. The other six overlap existing entries (`apiFetch` auth, money formatter, plural-neutral i18n, NSwag enum schema, `[ProducesResponseType]` discipline, `ApiError.fields` flattening) — those need cross-references added so the catalog stays the source of truth. Separate ticket; not deferred indefinitely.
- **ADR-emergence-from-review pattern** — Sprint 6 produced one ADR (0024) from review feedback rather than up-front planning. Worth formalising as either a new entry in `docs/processes/` or a short ADR (0025) that documents the workflow: when a review surfaces an architectural gap, draft an ADR before re-implementing.

### What I'd do differently next sprint

- **If Phase 4 has parallel tickets, commit to stacked PRs or wait-for-merge discipline at sprint start.** T-0046 and T-0049 both required hand-reconciliation of shared helper files (`catalog.ts`) because branches were authored in parallel against stale trees. Phase 4 (Orders) has long dep chains; if T-0063 / T-0066 / T-0067 end up in flight simultaneously, pick one of the two strategies up front.
- **Don't trust "this is mechanical" — budget review cycles accordingly.** T-0049ab was nominally three attribute lines; it actually shipped a platform-wide enum schema transformer + a controller schema-collision workaround + a generator-script appendix, after three Copilot rounds. Implication for sprint planning: attribute-only / spec-only PRs need the same review budget as feature PRs, not a half-cycle estimate.
- **Verify spec-to-implementation drift before opening the PR.** T-0048's ticket had five drifts that Copilot caught after the fact. A pre-PR self-review of the AC list against the shipped commit (not the spec at ticket-expansion time) would have closed them without a review round.
