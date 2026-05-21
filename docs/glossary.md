# Glossary

> Draft. BA will refine during discovery.

| Term | Definition |
|---|---|
| **Maker** | A business listed on the platform offering production services (3D printing, textile, laser, etc.). |
| **Customer** | An end-user purchasing from a maker. |
| **Order** | A contract between customer and maker for a product/service, brokered by the platform. |
| **Order number** | Human-readable identifier, namespaced per country, e.g. `T-CZ-20260001`. |
| **Packet** | A Zásilkovna shipment associated with an order; has `packet_id` and tracking URL. |
| **Pickup point** | Zásilkovna branch (Z-Point or Z-Box) where the customer collects the packet. |
| **Personal pickup** | Direct handover at the maker's address; alternative to Zásilkovna. |
| **Platform fee** | 15% of product price, retained by JVM YORE; deducted from maker payout. |
| **Maker payout** | `product_price − platform_fee + shipping_price`. Paid in the weekly batch. |
| **Payout batch** | Weekly admin job: collects `delivered` orders, generates fee invoices, exports CSV for bulk bank transfer. |
| **Customer invoice** | PDF: JVM YORE → customer; issued at payment. |
| **Fee invoice** | PDF: JVM YORE → maker; issued at payout batch; covers commission for N orders. |
| **Escrow** | The state between `paid` and `completed`: platform holds the money until the order is delivered (or auto-delivered after 7 days). |
| **Auto-deliver** | Cron job that flips `shipped` → `delivered` 7 days after `shipped_at` if customer hasn't confirmed. |
| **ARES** | Czech state company registry; source of truth for IČO → company data. |
| **IČO** | Czech 8-digit business ID. |
| **DIČ** | Czech VAT ID (`CZ` + 8–10 digits). Optional; only VAT-registered makers have one. |
| **RLS** | Postgres Row Level Security; how Supabase scopes data per user/role. |
| **Country code** | ISO 3166-1 alpha-2; `CZ` at launch. |
