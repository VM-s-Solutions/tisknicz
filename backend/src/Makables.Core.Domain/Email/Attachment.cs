namespace Makables.Core.Domain.Email;

/// <summary>
/// One inline file payload carried alongside an <see cref="EmailMessage"/>.
/// Optional — most emails (auth flows, maker notifications) carry none;
/// the customer-facing <c>order.paid.customerEmail</c> branch (T-0069)
/// attaches the rendered invoice PDF.
///
/// <para>
/// <b>Single-attachment shape</b> per T-0069 locked decision 8. Multi-
/// attachment is YAGNI at MVP — every current sender attaches at most
/// one file (the invoice PDF), so the field is <c>Attachment?</c> on
/// <see cref="EmailMessage"/>, not <c>IReadOnlyList&lt;Attachment&gt;</c>.
/// Future tickets can extend to a list without breaking existing senders
/// when the second concrete use case arrives.
/// </para>
///
/// <para>
/// <b>Bytes, not a stream.</b> The producer (<see cref="Features.Email"/>'s
/// <c>EmailSendService</c>) already downloaded the blob into memory before
/// building the attachment. SendGrid's SDK base64-encodes the bytes for
/// the wire; a stream-shaped surface would add no value (no incremental
/// upload) and force the provider to know about lifetimes.
/// </para>
///
/// <para>
/// <b>Provider-agnostic.</b> <see cref="IEmailProvider"/> implementations
/// translate this shape to their SDK's attachment API
/// (e.g. <c>sgMessage.AddAttachment(filename, base64, mime)</c> for
/// SendGrid). No blob / storage knowledge in the provider — that
/// orchestration sits with <c>EmailSendService</c> per ADR adapter
/// discipline.
/// </para>
/// </summary>
public sealed record Attachment
{
    public Attachment(string Filename, byte[] Bytes, string MimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Filename);
        ArgumentNullException.ThrowIfNull(Bytes);
        if (Bytes.Length == 0)
            throw new ArgumentException("Attachment bytes must be non-empty.", nameof(Bytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(MimeType);

        this.Filename = Filename;
        this.Bytes = Bytes;
        this.MimeType = MimeType;
    }

    /// <summary>Display name the recipient sees (e.g. <c>faktura-M-CZ-20260042.pdf</c>).</summary>
    public string Filename { get; init; }

    /// <summary>Raw file bytes — the provider base64-encodes for the wire.</summary>
    public byte[] Bytes { get; init; }

    /// <summary>MIME type (e.g. <c>application/pdf</c>).</summary>
    public string MimeType { get; init; }
}
