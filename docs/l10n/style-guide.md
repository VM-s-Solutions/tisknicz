# L10n style guide — cs-CZ

Single source of truth for tone, formatting, and key-naming conventions in
`frontend/src/lib/i18n/cs-CZ.ts`. Czech-only at launch (CLAUDE.md §i18n);
this guide exists so a second locale is a sibling catalog, not a rewrite.

## Formality

- **Vykání (V form)** for customer- and admin-facing copy — the default
  everywhere unless a screen is explicitly maker-only.
- **Tykání (T form)** for maker-facing copy is still pending business
  confirmation (see `docs/questions/open.md`); until resolved, maker
  dashboard strings currently in the catalog use vykání too — do not
  introduce tykání ad hoc in a single PR.

## Formatting

- **Currency:** `1 234 Kč` — space thousands separator, suffix, no haléře.
- **Dates:** Czech short format, `9. 5. 2026`.
- **Buttons:** sentence case (`Přijmout objednávku`), never Title Case.
- **No anglicisms** where a Czech equivalent exists (`stáhnout`, not
  `downloadnout`).
- **Plural-neutral counts:** until `t()` learns `Intl.PluralRules`, any
  `{count}` interpolation uses the "Label: N" shape (`Objednávek: {count}`)
  to dodge the Czech genitive-plural trap (1 / 2–4 / 0,5+ / fractional all
  take different noun forms otherwise).

## Key naming

- Dot-notation, grouped by domain (`auth.*`, `catalog.*`, `order.*`,
  `dashboard.<audience>.*`).
- Keys are **semantic**, not literal text (`order.detail.button.confirmReceived`,
  not the Czech string itself).
- **Error-code parity (patterns.md §A.4):** for any key that backs a
  `BusinessErrorMessage` code, the i18n key's dot-path must match the
  backend constant's code string **exactly** — `resolveErrorMessage` maps
  the dotted code 1:1, with no per-feature prefix inserted in between.
  Example: backend `BusinessErrorMessage.AuthOAuthInvalidState =
  "auth.oauthInvalidState"` → frontend key `'auth.oauthInvalidState'`,
  *not* `'auth.someFeature.oauthInvalidState'`.
- **Shared/provider-agnostic error codes must not be duplicated per
  caller.** If two features raise the same `BusinessErrorMessage` code
  (e.g. Google and Apple OAuth both raise `AuthOAuthEmailNotVerified`),
  they share one i18n key at that code's exact path. Only the
  UI-chrome copy that is genuinely provider/feature-specific (button
  labels, dividers, feature-local fallback messages) gets a
  feature-scoped namespace (`auth.apple.signInButton`, not
  `auth.apple.oauthEmailNotVerified`).
- Non-error-code UI strings are free to use a descriptive namespace per
  screen/component (`dashboard.maker.products.form.field.title`).

## Review checklist per PR

1. Every new user-facing string has a semantic key — no literal Czech
   inline in components.
2. Every key backing a `BusinessErrorMessage` code matches that code's
   dot-path exactly; check `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs`
   before approving.
3. Wording reads as natural Czech, not a machine-translated calque of
   the English original.
4. No `TODO(l10n)` markers left in a merged PR.
