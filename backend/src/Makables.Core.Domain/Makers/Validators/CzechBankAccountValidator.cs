namespace Makables.Core.Domain.Makers.Validators;

/// <summary>
/// Czech bank account format + ČNB mod-11 checksum check (US-maker-0003
/// AC-3). Pure domain helper — no DI, no async — so the FluentValidation
/// rule on <c>UpdateMakerProfile.Command</c> can call it directly.
///
/// <para>
/// Canonical form: <c>[prefix-]number/bankCode</c>
/// </para>
/// <list type="bullet">
///   <item><description><c>prefix</c> — optional, 1..6 digits.</description></item>
///   <item><description><c>number</c> — 2..10 digits.</description></item>
///   <item><description><c>bankCode</c> — exactly 4 digits.</description></item>
/// </list>
///
/// <para>
/// Both <c>prefix</c> and <c>number</c> carry the ČNB-defined mod-11
/// weighted checksum (weights, right-to-left, are
/// <c>1, 2, 4, 8, 5, 10, 9, 7, 3, 6</c>). The weighted sum must be
/// divisible by 11. Pinned by <c>CzechBankAccountValidatorTests</c>
/// against the canonical examples from the ČNB rulebook.
/// </para>
/// </summary>
public static class CzechBankAccountValidator
{
    // Weights are applied right-to-left over up to 10 digits.
    private static readonly int[] Weights = [1, 2, 4, 8, 5, 10, 9, 7, 3, 6];

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || slash != trimmed.LastIndexOf('/')) return false;

        var accountPart = trimmed[..slash];
        var bankCode = trimmed[(slash + 1)..];

        if (bankCode.Length != 4 || !IsAllDigits(bankCode)) return false;

        string prefix, number;
        var dash = accountPart.IndexOf('-');
        if (dash < 0)
        {
            prefix = string.Empty;
            number = accountPart;
        }
        else
        {
            if (dash != accountPart.LastIndexOf('-')) return false;
            // T-0034 Copilot review: when a dash is present, BOTH sides
            // must carry digits. Inputs like "-2000145399/0100" (empty
            // prefix) or "19-/0100" (empty number) previously slipped
            // past because IsAllDigits("") returns true. Reject them up
            // front so the prefix/number length rules below are reached
            // only for the well-formed "X-Y" shape.
            if (dash == 0 || dash == accountPart.Length - 1) return false;
            prefix = accountPart[..dash];
            number = accountPart[(dash + 1)..];
        }

        // Prefix length: 0 when no dash is present (accepted), 1..6
        // when a dash is present (the dash == 0 guard above rules out 0).
        if (prefix.Length > 6 || !IsAllDigits(prefix)) return false;
        if (number.Length is < 2 or > 10 || !IsAllDigits(number)) return false;

        return ChecksumOk(prefix) && ChecksumOk(number);
    }

    private static bool ChecksumOk(string digits)
    {
        if (digits.Length == 0) return true;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            sum += d * Weights[i];
        }
        return sum % 11 == 0;
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }
}
