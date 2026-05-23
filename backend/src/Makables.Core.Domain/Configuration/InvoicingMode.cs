namespace Makables.Core.Domain.Configuration;

/// <summary>
/// Per-country invoicing mode. Drives <c>InvoiceService</c>'s branch per
/// ADR 0013 (enforcement-mode pattern) and patterns §A.13. New modes are
/// additive — existing modes never change behavior.
/// </summary>
public enum InvoicingMode
{
    /// <summary>No VAT, no fiscal reporting. Maker is not a VAT payer.</summary>
    None = 0,

    /// <summary>Maker is VAT-registered; invoice carries VAT lines.</summary>
    StandardVat = 1,

    /// <summary>B2B intra-EU reverse charge (future).</summary>
    ReverseCharge = 2,

    /// <summary>
    /// Strict fiscal reporting (CZ EET 2.0, DE TSE, AT RKSV) — receipt held
    /// until signature obtained. Not used at MVP launch.
    /// </summary>
    StrictFiscalReporting = 3,
}
