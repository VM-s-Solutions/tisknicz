# Makables — Launch checklist

Blocking pre-launch action items. Each line is gated; go-live is blocked until
every BLOCKING item is resolved. Maintained alongside the tickets that surface
the gap (the ticket scaffolds the route/feature; the line tracks the missing
input that only the operator can supply).

## Legal

- [ ] **Legal text (Q-0030, BLOCKING):** JVM YORE s.r.o. must supply approved
  VOP (obchodní podmínky) + GDPR privacy/cookie text. Pages `/vop` + `/gdpr` are
  scaffolded (shell + nav-reachable route + i18n keys + a visible placeholder
  banner) by T-0130; only the legal TEXT is missing. Before go-live: replace the
  `static.legal_placeholder.banner` Alert and populate the `static.terms.*` /
  `static.privacy.*` keys with the approved text. See `docs/questions/open.md`
  Q-0030 (incl. the open sub-question on a cookie-consent banner / cookie
  management UI — confirm whether launch needs one).

## SEO (T-0131)

- [ ] **Site URL env:** set `NEXT_PUBLIC_SITE_URL=https://makables.cz` in the
  production/staging environment (the canonical-host base for
  sitemap/robots/canonical/og:url; read only via `lib/seo/site-url.ts`).
  Defaults to `https://makables.cz` at build time; localhost is the dev
  default. After deploy, verify `/sitemap.xml` + `/robots.txt` resolve and
  submit the sitemap to Google Search Console.
- [ ] **OG image asset (follow-up, non-blocking):** add a brand OG image
  (`frontend/public/og-default.png`, 1200×630) and wire it into
  `lib/seo/site-url.ts` so every page inherits a `summary_large_image` card.
  MVP ships text-only `summary` cards (no image asset exists yet).
- [ ] **Product sitemap enumeration (deferred):** `/produkt/{productId}` URLs
  are NOT in the sitemap at MVP — there is no bulk product-id read (products
  are reachable only through a maker profile). Maker profiles
  (`/katalog/{slug}`) ARE enumerated. A backend bulk-id feed would enable
  product enumeration post-MVP.
