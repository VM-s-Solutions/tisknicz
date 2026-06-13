# Gate 3 (Security) — payout-core bundle (T-0101 / T-0102a / T-0102b / T-0104, Q-0017)

**Branch:** `feat/order-cleanup-bundle` (9 commits over master)
**Scope:** money aggregation + financial-document generation + bank-account CSV
**Reviewer:** Security & DevOps
**Date:** 2026-06-13

## Verdict: FOLD

One Medium output-encoding finding (CSV injection / delimiter injection on the
bank-transfer CSV). Not a BLOCK — the source field is ARES-derived, not
free-form maker input, so exploitability is low — but it is a real
defense-in-depth gap on a financial artifact an admin opens in Excel, and the
fix is small and localized. Authz, IDOR, PII-isolation, amount-integrity, and
migration checks all PASS.

---

## Headline: CSV injection / delimiter injection (MEDIUM)

`GenericPayoutCsvFormatter.Format` (`backend/src/Makables.Core.AppServices/Features/Payouts/GenericPayoutCsvFormatter.cs`)
streams every field **verbatim** with no escaping and no formula-char
neutralization:

```csharp
sb.Append(line.BankAccount).Append(Delimiter)
  .Append(amount).Append(Delimiter)
  .Append(vs).Append(Delimiter)
  .Append(message).Append(Crlf);   // message = "{batchNumber} {companyName}"
```

Two distinct defects in the `message` column (which carries `MakerCompanyName`):

1. **Formula injection.** A company name beginning with `= + - @` (or tab/CR)
   becomes a live formula when the admin opens the CSV in Excel/LibreOffice
   (e.g. `=HYPERLINK(...)`, `=cmd|'/c ...'!A1`, DDE). The formatter does not
   prefix `'` or strip the leading char.
2. **Delimiter / row injection.** A company name containing `;` or a CR/LF
   would split the row into extra columns or inject a whole new payment row —
   directly corrupting a file that drives **real bank transfers**. The 140-char
   truncation does not address this.

**Mitigating factor (why MEDIUM, not High/BLOCK):** `MakerCompanyName` is the
**ARES registry snapshot**, not free-form input. Makers cannot edit it
(`UpdateMakerProfile` AC-2 holds it read-only; it is set from the ARES company
record at registration and admin-only ARES refresh). So an attacker needs a
real Czech company whose *official registry name* begins with a formula char or
contains `;`/CRLF — low likelihood, but ARES names are not an allowlisted
charset and legitimately contain `+ - & /`.

**Required fix (output-boundary, not input):** in the formatter, for any
non-numeric/text column (here `message`): (a) if the value starts with
`= + - @ \t \r`, prefix with `'` or a leading apostrophe-space per OWASP CSV-
injection guidance; (b) RFC-4180-quote any field containing the delimiter,
quote, CR, or LF (wrap in `"`, double interior `"`). `BankAccount` and `amount`
are server-shaped (`[prefix-]number/bankCode`, invariant `0.00`) so are not the
attack surface, but quoting them too is cheap and correct. Add golden tests:
`=cmd`, `@SUM`, a name with `;`, a name with embedded CRLF.

No existing test covers any of this — `GenericPayoutCsvFormatterTests` only
exercises benign `'A'`/`Alpha s.r.o.` names.

---

## Checklist results

| # | Check | Result | Note |
|---|-------|--------|------|
| 1 | CreatePayoutBatch authz | PASS | `[Authorize]` on admin host; host accepts only `MakablesAudiences.Admin` (`AcceptedAudiencesFor` → `[Admin]`); command is parameterless so no client-controlled scope — claims all eligible for the default country only; fail-closed `session.GetUserId()` null-check first (money never attributed to "system"). Attacker cannot widen scope. |
| 2 | CSV download authz / IDOR | PASS | `GET /payout-batches/{id}/csv` lives **only** on the admin host (no maker/customer/public host references payout/CSV/BankAccount — grep clean). Admin audience enforced. Streams from the private `payouts` container through the host (no direct browser→blob). `Cache-Control: private, no-store` + `Content-Disposition: attachment` + `Content-Type: text/csv` all present. `GetByIdUnscopedAsync` is acceptable — the host audience *is* the tenant boundary for admin. |
| 3 | Bank account / CSV injection | **FOLD** | See headline. BankAccount itself is server-shaped (safe); the `message`/company column is the exposure. |
| 4 | Fee invoice content | PASS | `ProvizniDokladDocument` renders issuer = platform (`IssuerName/Ico/Dic`), recipient = maker (`CompanyName/RegistrationNumber/VatId/Email`), line items = per-order numbers + fee only. **No customer name/email/address** anywhere on the fee invoice. Owner-correct (platform→maker). |
| 5 | Q-0017 migration | PASS | Static `REPLACE` SQL, literal tokens, idempotent guards. No injection, no data exposure. |
| 6 | Blob paths | PASS | CSV `payouts/{cc}/{batchNumber}.csv`; fee PDF `invoices/{cc}/payouts/{batchId}/{invoiceNumber}.pdf`. Deterministic, server-derived; no user-controlled path segment → no path traversal. |
| 7 | Outbox payload | PASS | `PayoutFeeInvoiceMakerEmail` recipient = the maker's own `makerUser.Email`, resolved per-maker inside the loop. No cross-maker leak. |
| 8 | Audit posture | PASS (note) | `payoutBatch.create` audit `afterJson` is a run-summary: batch number, totals, counts, artifact flags. **No bank accounts, no per-maker rows** in the audit JSONB. Good. (If per-maker bank data is ever added to audit later, flag it — admin-audit is admin-readable, acceptable, but keep PII out by default.) |
| 9 | Amount integrity | PASS | Totals server-computed: `eligible.Sum(MakerPayoutAmountMinor)`, fee = `Sum(PlatformFeeAmountMinor)`, CSV amount = `Sum(MakerPayoutAmountMinor)` from claimed-order snapshots. Currency-homogeneity guard rejects mixed-currency batches. No client-supplied amount anywhere in claim or CSV. |

### Adjacent observations (non-blocking)
- **T-0104 function HTTP trigger** uses `AuthorizationLevel.Function` (function key) — satisfies the cron-secret rule. Timer trigger is internal. OK.
- **Idempotency** is solid: open-batch re-run returns the same row (`AlreadyExisted`), week-guard + unique index, artifact re-entrancy resumes rather than duplicating. Blob overwrite-safe. Webhook-retry-equivalent safety holds.
- Admin audience is accepted on the customer/maker/public hosts too (by design — admin acts cross-host), but the CSV/create endpoints exist only on the admin host, so this does not widen the bank-data surface.

---

## Required before PASS
1. Neutralize CSV formula chars (prefix `'` / strip leading `= + - @ \t \r`) **and** RFC-4180-quote any field containing `;` `"` CR LF in `GenericPayoutCsvFormatter`, applied at minimum to the `message`/company column.
2. Add golden tests for `=cmd`, `@SUM(...)`, `;`-bearing, and CRLF-bearing company names.

Re-submit to Gate 3 after (1)+(2); everything else is cleared.
