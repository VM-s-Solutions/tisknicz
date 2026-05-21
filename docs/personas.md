# Personas

> Status: drafted and confirmed during Phase 1 — Batch 1. Update via PR.

## Customer

**Mixed B2C + B2B at launch — single unified flow.**

| Trait | Detail |
|---|---|
| Type | Czech individuals **and** small businesses |
| B2C use cases | Gifts, personal projects, hobby parts, replacement parts, custom textile, decoration |
| B2B use cases | Signage, prototypes, marketing items (vizitky, bannery), branded textile, packaging samples |
| Geography | Czech Republic (CZ) |
| Language | Czech only at launch |
| Payment | Online card or bank button via Comgate; no cash, no NET terms at launch |
| Delivery | Zásilkovna pickup point (default) or personal pickup at maker |
| Identity | Email + password (or magic link) |
| Optional B2B fields on order | Company name, IČO, DIČ (for invoice) — never required to place an order |
| Pre-purchase contact | **Not allowed at launch.** Customer must place + pay an order before messaging the maker (escrow model). Pre-purchase inquiries are a post-MVP question. |

**Design implications**
- One order form, optional business fields. No separate B2C/B2B flows.
- Invoice generation respects the optional IČO/DIČ fields.
- B2B order value distribution skews higher → product price field accepts up to a reasonable cap (TBD by Architect ADR; sketch: max 1,000,000 CZK).

**Out of scope at launch**
- NET-30 / invoice-then-pay terms
- PO numbers
- Custom-quote / RFQ flow
- Subscription / repeat orders
- Multi-user customer accounts (e.g. company with several buyers)

---

## Maker

**Hybrid catalog: solo makers + 1–2 anchor workshops. Single maker model for both.**

| Trait | Detail |
|---|---|
| Long tail | Solo OSVČ, hobbyist-turned-pro, 1–3 machines, 5–30 orders/month, home or small workshop |
| Anchor accounts | 1–2 established workshops per top category (3D print, textile) seeded by admin to guarantee supply |
| Legal form | Czech business with IČO. OSVČ or s.r.o. |
| VAT status | Optional — most solos are not VAT payers; workshops may be. Invoice generation must handle both. |
| Bank account | Czech format `123456789/0100`, validated server-side |
| Identity | Email + password (or magic link), single user per maker account at launch |
| Onboarding | IČO lookup → ARES fills company data; maker confirms, adds bio + categories + bank account |
| Verification | Active immediately; admin can flip `is_verified=true` badge later |
| Tech comfort | Comfortable with the web, not enterprise. Will not tolerate complex CRMs. |
| Motivation | Traffic + escrow + no-paperwork. Platform handles invoicing, payouts, disputes. Maker focuses on production. |

**Design implications**
- One maker = one user account at launch. **Multi-user per maker is post-MVP** — flagged in `docs/questions/open.md` as a known future need (workshops will eventually want multiple operators).
- Workshop seeding is a manual onboarding task for admin, not a separate product feature.
- Maker dashboard must be usable on mobile (workshop operators often check from the floor).

**Out of scope at launch**
- Multiple users per maker account
- Maker-side staff roles (operator vs. owner)
- Maker-to-maker subcontracting
- Maker-owned shipping accounts (the platform's Zásilkovna account handles all shipments)

---

## Admin (platform operator — JVM YORE s.r.o.)

**You + 1 assistant. Daily check-ins.**

| Trait | Detail |
|---|---|
| Headcount | 2 people sharing the admin role |
| Cadence | Daily light checks (new disputes, suspicious orders, maker verifications); weekly heavy task (payout batch) |
| Tasks | Verify makers, run weekly payout batch + CSV export, handle disputes, refund/cancel edge cases, monitor for fraud, manage categories, manage `CountryConfiguration` |
| Authority | Same permissions for both admins at launch (single `admin` role). Audit trail on every admin action so they can see each other's work. |
| Tech comfort | Engineer-level (you) + non-engineer (assistant). Admin UI must be usable by both. |

**Design implications**
- Single `admin` role in MVP; no role split (support / finance / moderator) yet.
- Every admin action writes an audit log entry (`actor_user_id`, `action`, `target_entity`, `target_id`, `timestamp`, `notes`) — the `Auditable` columns aren't enough for admin actions on someone else's data.
- Admin dashboard prioritizes the **weekly payout batch flow** (the highest-stakes recurring action) and the **daily inbox** (new orders/disputes/makers to verify).

**Out of scope at launch**
- Role splits (support, finance, content moderator)
- Permission granularity (e.g. "can verify makers but not run payouts")
- Multi-tenant admin (different admins for different countries) — schema-ready via `country_code`, but UI single-country at launch

---

## Trust model

**Platform-as-escrow. No pre-purchase contact.**

- Customer → places order → pays Comgate → platform holds funds → maker accepts → maker ships → customer (or auto-deliver) confirms → payout to maker (weekly batch).
- Pre-purchase customer ↔ maker messaging is **disabled at launch**. Order-scoped messaging starts the moment the order is paid.
- This maximizes platform stickiness and protects both parties from off-platform deals.
- Risk: products with high customization need pre-purchase Q&A. We accept this for MVP; on-request products with a custom-quote flow are flagged for post-MVP (`docs/questions/open.md` Q for the Architect: how does the order state machine accommodate this later without rework?).

---

## Personas NOT covered at launch

- **Customer support agent** — admin assistant fills this role manually for MVP.
- **Content moderator** — admin handles reports of inappropriate products manually.
- **Finance / accountant** — payouts and invoices are automated; an external accountant (yours) reads exported CSVs and PDFs from the admin UI.
