namespace Makables.Core.Domain.Registry.Validators;

/// <summary>
/// Czech IČO (8-digit registration number) format + checksum check per
/// ADR 0018 §"Validation before lookup". Pure domain helper — no DI,
/// no async — so feature validators / lookup handlers can call it
/// before consuming the ARES rate-limit budget.
///
/// Algorithm (mod-11 weighted sum):
/// <list type="number">
///   <item><description>Reject anything that isn't exactly 8 digits (whitespace trimmed beforehand).</description></item>
///   <item><description>Take digits d1..d8 (d8 is the checksum).</description></item>
///   <item><description>Compute <c>s = sum(d_i * w_i)</c> for i = 1..7 with weights 8, 7, 6, 5, 4, 3, 2 (in code: 0-indexed loop i=0..6 with weight <c>8 - i</c>).</description></item>
///   <item><description>Compute <c>m = s mod 11</c>.</description></item>
///   <item><description>Expected checksum c:
///     <list type="bullet">
///       <item><description>m = 0  → c = 1</description></item>
///       <item><description>m = 1  → c = 0</description></item>
///       <item><description>m = 10 → c = 1   (Czech IČO does not use letter checksum)</description></item>
///       <item><description>else   → c = 11 - m</description></item>
///     </list></description></item>
///   <item><description>Compare c with d8.</description></item>
/// </list>
///
/// The algorithm is defined by Czech state administration; the cases
/// above are the standard fallbacks. Pinned by
/// <c>CzechIcoValidatorTests</c> against real ARES sample IČOs.
/// </summary>
public static class CzechIcoValidator
{
    /// <summary>
    /// True when <paramref name="ico"/> is exactly 8 digits AND the
    /// mod-11 checksum digit matches. Pads with leading zeros are NOT
    /// auto-applied — ARES expects the canonical 8-digit form. If you
    /// have a user input with a 7-digit IČO (rare, legacy), normalise
    /// to 8 digits BEFORE calling this.
    /// </summary>
    public static bool IsValid(string? ico)
    {
        if (string.IsNullOrEmpty(ico) || ico.Length != 8) return false;

        Span<int> digits = stackalloc int[8];
        for (var i = 0; i < 8; i++)
        {
            var c = ico[i];
            if (c < '0' || c > '9') return false;
            digits[i] = c - '0';
        }

        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            sum += digits[i] * (8 - i);
        }
        var mod = sum % 11;
        var expectedCheck = mod switch
        {
            0 => 1,
            1 => 0,
            10 => 1,
            _ => 11 - mod,
        };

        return digits[7] == expectedCheck;
    }
}
