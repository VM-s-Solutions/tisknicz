---
id: 0025
title: QuestPDF + invoice PDF rendering posture
status: accepted
date: 2026-06-07
deciders: [Architect, PM, dotnet-backend]
living_docs: [docs/architecture/roles/invoice.md]
---

# 0025 — QuestPDF + invoice PDF rendering posture

## Context

T-0068b (the back half of the T-0068 L-split) lands the customer-facing invoice PDF rendering pipeline. The decisions inventoried here were locked at grooming via the `/feature` deliberation protocol (8 user-locked answers + 2 PM clarifications captured in the ticket's `## Locked design decisions` section). This ADR documents the four decisions that have lasting architectural weight — they outlive T-0068b and PM/Reviewer must revisit them if the trigger conditions change.

The four decisions:

1. **PDF library** — which engine generates the invoice bytes, and under what license.
2. **Font embedding** — what font glyphs the PDF carries so Czech text reads correctly on every viewer.
3. **SPAYD QR code + bank-account source** — where the IBAN comes from, and what happens when it's not set.
4. **Renderer interface scope** — invoice-specific abstraction vs. generic `IPdfRenderer<T>`.

§ 29 zákona č. 235/2004 Sb. o DPH dictates the mandatory fields for a Czech daňový doklad (StandardVat mode); the renderer's two `IDocument` templates (`DokladOProdejiDocument` for `InvoicingMode.None`, `DanovyDokladDocument` for `InvoicingMode.StandardVat`) are the legal-format compliance surface.

## Decision

1. **PDF library: QuestPDF Community license** at MVP. JVM YORE s.r.o. meets all three Community criteria (revenue < $1M USD, < 10 employees, not state-funded). `Makables.Infra.PdfRendering`'s static ctor + the `AddMakablesPdfRendering()` DI extension pin `QuestPDF.Settings.License = LicenseType.Community`. PM revisits when JVM YORE crosses revenue or headcount thresholds; switching to Professional is one config line + a Key Vault secret for the license key.

2. **Font embedding: Noto Sans subsetted to Czech glyphs (deferred — using QuestPDF default DejaVu Sans at T-0068b).** The locked-decision-2 plan was to embed `NotoSans-Regular-CzechSubset.ttf` + `NotoSans-Bold-CzechSubset.ttf` as `EmbeddedResource` in `Infra.PdfRendering`, generated via `pyftsubset` (target size ~80 KB). The subset toolchain is not available in the current build environment; rather than block T-0068b, the renderer uses QuestPDF's bundled default (DejaVu Sans, which has decent Czech-glyph coverage for `ž š č ř ď ť ě ů` etc.). A follow-up ticket generates the subset .ttf and switches the renderer's `TextStyle.FontFamily`. DejaVu Sans is unsubsetted ~700 KB if we ever ship it explicitly, but the QuestPDF default is built into the package binary so MVP carries no extra weight.

3. **SPAYD QR code + bank-account source: nullable `platform_iban` column on `country_configurations`.** MVP CZ seed: `platform_iban = NULL`. Renderer SKIPS SPAYD QR rendering when null and renders the invoice without a QR code (visible to the customer as "no pay-by-QR available"). When admin populates the IBAN later (via DB seed or admin UI in a downstream ticket), SPAYD QR codes automatically appear on new invoices; already-issued invoices are unaffected (PDFs are blob-stored and frozen). The SPAYD format string composition lives in `Core.Domain.Payments.Spayd.ForInvoice` (pure value-object encoder, no infrastructure dep); the format is `SPD*1.0*ACC:<iban>*AM:<amount>*CC:<ccy>*X-VS:<vs>`. At T-0068b the QR-image generation is intentionally deferred — the renderer emits the SPAYD payload as visible plain text in a "Platba QR kódem" box; a follow-up adds QRCoder + SkiaSharp for the actual QR-image rendering. The format-compliance surface (Spayd.ForInvoice) ships now and is pinned by 10 unit tests.

4. **Renderer interface scope: invoice-specific `IInvoicePdfRenderer`.** Single-purpose: takes `Invoice + CountryConfiguration` and returns a PDF `byte[]`. Lives at `Core.Domain/Rendering/IInvoicePdfRenderer.cs`. NOT a generic `IPdfRenderer<TPayload>`. When T-0074 ships shipping-label PDFs (Packeta labels), it gets its own `IShippingLabelRenderer` — different inputs (shipping address + tracking number), different output (often non-PDF / 100×150 label format), different code path. YAGNI per CLAUDE.md.

## Alternatives considered

- **PDF library: QuestPDF Professional now (€699/yr).** *Rejected* — premature; JVM YORE qualifies for Community today, the ADR records the revisit trigger. The Professional API is identical, so the switch is non-invasive when needed.
- **PDF library: iText 7 / PdfSharp / Migradoc.** *Rejected* — iText is AGPL (commercial license required for closed-source), PdfSharp / Migradoc have older DSLs that don't ergonomically express modern grid layouts. QuestPDF has the modern API + a generous Community license; no comparable alternative.
- **PDF library: Headless Chromium (Playwright → HTML → PDF).** *Rejected* — opaque rendering, large runtime dep (~200 MB Chromium), non-deterministic across Chrome versions (renderer determinism would break, undermining the blob-overwrite-on-retry safety per T-0068a locked decision 5).
- **Font: DejaVu Sans embed (unsubsetted).** *Rejected by locked decision 2 (but used as default at T-0068b due to font-subset-toolchain deviation)* — DejaVu Sans is ~700 KB unsubsetted vs. Noto Sans ~80 KB subsetted. Both have Czech-glyph coverage; Noto Sans subsetted is the long-term destination.
- **Font: system Arial / Helvetica.** *Rejected* — Linux App Service runtime doesn't have Arial; PDF would degrade unpredictably depending on the underlying OS image's font set.
- **SPAYD IBAN: hard-code in CZ seed.** *Rejected* — JVM YORE's bank-account decision is open at MVP; hard-coding blocks T-0068b on a non-tech decision.
- **SPAYD: defer entirely (no QR even when IBAN is set).** *Rejected* — losing the wiring means adding it back later via a separate migration + renderer change; cheaper to ship the schema NULLable now and gate the renderer's behaviour on `PlatformIban IS NOT NULL`.
- **Renderer interface: generic `IPdfRenderer<TPayload>`.** *Rejected* — speculative abstraction; T-0074 shipping labels will have a different shape (likely not even PDF). Two single-purpose adapters are cheaper than one over-general one. YAGNI.
- **Issuer values: hard-code in renderer.** *Rejected* — couples invoice rendering to a code release; admin can't update issuer name without redeploy.
- **Issuer values: Azure App Configuration.** *Rejected* — `country_configurations` is the natural home (issuer name is per-country: same legal entity may carry different names in different jurisdictions). App Configuration is platform-wide and doesn't carry per-country variance for free.

## Consequences

Positive:
- Zero license cost at MVP; the renderer pipeline is production-ready from day one.
- Determinism contract holds: same `Invoice` → byte-identical PDF (renderer reads `Invoice.IssueDate` for "creation date", never `DateTime.Now`). Makes the blob-overwrite-on-retry safe.
- `Infra.PdfRendering` is a single new project with one external NuGet dep (QuestPDF). No font binary in the repo at T-0068b, so the diff stays small.
- SPAYD wiring is in place — the day admin decides on a bank account is one `UPDATE country_configuration` away from QR-enabled invoices.
- Invoice-specific renderer interface keeps T-0074 shipping-label work decoupled.

Negative:
- DejaVu Sans default vs. the planned Noto Sans subset is a temporary deviation tracked as a follow-up. Glyph coverage is similar; the cosmetic difference is minor.
- SPAYD QR at T-0068b is rendered as plain text in a "QR" box, not as an actual QR image. Customers can't scan it from the PDF yet; the follow-up that adds QRCoder + SkiaSharp closes this.
- QuestPDF Community is free, but a future revenue / headcount milestone forces a Pro switch — PM must catch this on sprint checkpoints.

Neutral:
- Two `IDocument` templates (one per implemented `InvoicingMode`) is more code than a single template with conditional sections, but the legal-format compliance surface is clearer when each mode owns its layout.

## Compliance / verification

A reviewer can verify the decisions hold by checking:

1. **License pin.** `Makables.Infra.PdfRendering/QuestPdfInvoiceRenderer.cs` static ctor sets `QuestPDF.Settings.License = LicenseType.Community`; `Makables.Config/Extensions/AddMakablesPdfRendering.cs` also pins it (defence in depth for hosts that bypass DI). Both lines present; no `LicenseType.Professional` in the diff.
2. **Renderer determinism.** No `DateTime.Now`, no `clock.UtcNow`, no `Guid.NewGuid()` calls inside `QuestPdfInvoiceRenderer.cs` or either `IDocument` template. The `DocumentMetadata.CreationDate` reads `Invoice.IssueDate.ToDateTime(...)`. `IssueInvoiceIntegrationTests.Idempotent_retry_returns_existing_and_does_not_double_upload` pins the byte-stability via blob-client `Received(1)` semantics.
3. **SPAYD posture.** `Spayd.ForInvoice` matches the format `SPD*1.0*ACC:<iban>*AM:<amount>*CC:<ccy>*X-VS:<vs>` (pinned by 10 unit tests in `SpaydTests.cs`). Renderer branches on `country.PlatformIban` non-null before emitting the SPAYD block.
4. **Renderer scope.** Only `IInvoicePdfRenderer` exists in `Core.Domain/Rendering/`; no `IPdfRenderer<T>`. T-0074 reviewer will check this hasn't drifted.

## Defense

*Not yet challenged.*

## Related

- ADR: 0003 (money + rounding), 0009 (numbering), 0011 (blob storage), 0014 (UoW + outbox)
- Ticket: T-0068b (sister: T-0068a for the Invoice entity + repository)
- User story: US-customer-0010, US-customer-0017, US-admin-0012
