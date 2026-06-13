namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Format seam (Q1) for the bank-transfer CSV — keyed-service-ready per the
/// provider-adapter pattern so bank-native exporters (Fio/KB/ČSOB) become
/// one keyed implementation each, with zero handler changes, once the
/// operator names the bank.
///
/// <para>
/// <b>Generic documented format (§C.5), frozen as golden-file tests.</b>
/// The formatter returns a <see cref="string"/> (string-in/string-out keeps
/// golden tests trivial; the UTF-8 BOM is the artifact service's encoding
/// job). Spec:
/// <list type="bullet">
///   <item>Header row <c>account;amount;vs;message</c>.</item>
///   <item><b>CRLF</b> line endings, <b>semicolon</b> delimiter (Czech-locale
///     CSV convention).</item>
///   <item>One data row per maker, in the order the caller supplies
///     (deterministic — by company name then maker id).</item>
///   <item><c>account</c> = the maker's bank account verbatim (Czech
///     <c>[prefix-]number/bankCode</c>, already validated at claim time).</item>
///   <item><c>amount</c> = the summed maker payout rendered as
///     invariant-culture <c>0.00</c> decimal CZK (<c>123456</c> minor →
///     <c>1234.56</c>).</item>
///   <item><c>vs</c> = the digits of the batch number
///     (<c>VYP-CZ-2026-W24</c> → <c>202624</c>, ≤ 10 digits — the Czech VS
///     limit).</item>
///   <item><c>message</c> = <c>{batchNumber} {makerCompanyName}</c>
///     truncated to 140 chars.</item>
/// </list>
/// A blank bank account is a programmer error (the Q5 claim invariant
/// guarantees every row has a validated account) → the implementation
/// throws <see cref="ArgumentException"/>.
/// </para>
/// </summary>
public interface IPayoutCsvFormatter
{
    string Format(PayoutCsvBatch batch);
}
