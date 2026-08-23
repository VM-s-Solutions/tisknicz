using System.Text;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;

namespace Makables.Core.AppServices.Features.Email;

/// <summary>
/// The design tokens the transactional emails draw with, transcribed from
/// the LIGHT half of <c>frontend/src/app/globals.css</c> — the same nine
/// values <c>InvoiceTheme</c> carries, for the same reason: an email body
/// has no CSS layer to resolve a custom property through, and a mail
/// client cannot be trusted to follow a theme.
///
/// <para>
/// Light is a deliberate commitment rather than a default. Gmail and
/// Outlook apply their own colour inversion to dark-mode inboxes and do
/// it destructively; a design that declares one palette and states
/// <c>color-scheme: light</c> survives that far better than one that
/// tries to ship both. The ratio the design language mandates (~60 white
/// / 30 quiet neutral / 10 primary) holds: paper stays white, structure
/// is hairlines and near-black ink, and the teal appears only on the
/// wordmark, the masthead rule and the single call-to-action.
/// </para>
/// </summary>
internal static class EmailTheme
{
    public const string InkTitle = "#08191c";
    public const string InkBody = "#0e2429";
    public const string InkMuted = "#395459";
    public const string InkFaint = "#526f77";

    public const string Hairline = "#ccd8dc";
    public const string HairlineSoft = "#e7edef";
    public const string BandFill = "#f7f9fa";
    public const string Paper = "#ffffff";

    public const string Brand = "#00786c";
    public const string BrandLine = "#0d9488";

    /// <summary>
    /// System font stack. Web fonts are not loadable in most clients and
    /// a @font-face that silently fails costs a render pass for nothing.
    /// </summary>
    public const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";
}

/// <summary>
/// Renders the branded HTML part of a transactional email.
///
/// <para>
/// The input is the email's <em>already-substituted</em> plain-text body
/// — the same string that ships as the <c>text/plain</c> alternative.
/// Copy therefore stays in <c>email_template_translations</c> (one source
/// of truth, per language, editable without a deploy) and this class owns
/// only presentation: the shell, the typography and the promotion of the
/// bare action URL into a real button. Adding a template type costs
/// nothing here.
/// </para>
///
/// <para>
/// SECURITY: every value that reaches the output is escaped, and only an
/// absolute <c>http</c>/<c>https</c> URI is ever allowed to become an
/// <c>href</c>. That matters because the body can legitimately contain
/// user-supplied free text (dispute descriptions, admin resolution
/// notes) which <c>EmailSendService</c> already neutralizes for
/// placeholder syntax but not for markup — the plain-text pipeline had no
/// need to. The escaping here is what makes an HTML part safe to add on
/// top of it.
/// </para>
/// </summary>
public static class EmailHtmlLayout
{
    private const int MaxPreheaderLength = 140;

    /// <summary>Legal footer identity — the operator behind the brand (CLAUDE.md §"Operator").</summary>
    private const string OperatorLine = "JVM YORE s.r.o.";

    /// <summary>
    /// Build the HTML part for one email.
    /// </summary>
    /// <param name="heading">Rendered subject; doubles as the H1.</param>
    /// <param name="plainTextBody">The substituted plain-text body.</param>
    /// <param name="ctaLabel">Button label for the action URL found in the body.</param>
    /// <param name="languageCode">Recipient language; drives the fallback-link and footer chrome.</param>
    public static string Render(
        string heading,
        string plainTextBody,
        string? ctaLabel,
        string languageCode)
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(plainTextBody);

        var isEnglish = string.Equals(languageCode, LanguageCode.EnUS, StringComparison.OrdinalIgnoreCase);
        var blocks = SplitIntoBlocks(plainTextBody);
        var actionUrl = blocks.Select(TrailingUrl).FirstOrDefault(u => u is not null);

        var body = new StringBuilder();
        var afterCta = false;

        foreach (var block in blocks)
        {
            if (!afterCta && actionUrl is not null && TrailingUrl(block) == actionUrl)
            {
                // The lead-in ("Detail objednávky najdete na:") stays a
                // paragraph and the URL it introduces becomes the button.
                var lede = block[..^actionUrl.Length].TrimEnd();
                if (lede.Length > 0) AppendParagraph(body, lede, muted: false);

                AppendButton(body, actionUrl, ctaLabel ?? DefaultCtaLabel(isEnglish));
                afterCta = true;
                continue;
            }

            // Everything past the button is small print — expiry, "ignore
            // this if it wasn't you". Muting it is what makes the one thing
            // the recipient has to do read as the one thing on the page.
            // Any further URL falls through to here and becomes a plain
            // inline link rather than a competing button.
            AppendParagraph(body, block, muted: afterCta);
        }

        // The copy-paste escape hatch goes last, under the small print:
        // it exists for the minority whose client mangles anchors, and
        // putting a 60-character token band between the button and the
        // expiry line pushes the terms out of the first screen.
        if (actionUrl is not null)
            AppendFallbackLink(body, actionUrl, isEnglish);

        var preheader = BuildPreheader(blocks);
        return Shell(heading, body.ToString(), preheader, isEnglish);
    }

    // === Block model =====================================================

    /// <summary>
    /// Split the plain-text body into display blocks on blank lines, and
    /// drop the trailing "Makables — makables.cz" sign-off: the shell's
    /// own footer carries the brand, and printing it twice reads as a
    /// templating accident.
    /// </summary>
    private static List<string> SplitIntoBlocks(string plainTextBody)
    {
        var normalized = plainTextBody.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim('\n', ' ', '\t'))
            .Where(b => b.Length > 0)
            .ToList();

        if (blocks.Count > 0 && IsBrandSignOff(blocks[^1]))
            blocks.RemoveAt(blocks.Count - 1);

        return blocks;
    }

    private static bool IsBrandSignOff(string block) =>
        block.StartsWith("Makables", StringComparison.Ordinal)
        && block.Contains("makables.cz", StringComparison.OrdinalIgnoreCase)
        && block.Length < 60;

    /// <summary>
    /// The absolute http(s) URL that ENDS a block, or <c>null</c>.
    ///
    /// <para>
    /// Two copy shapes exist in the catalog and both have to work. The
    /// auth templates put the action link alone on its own line; the order
    /// and payout templates end a sentence with it
    /// ("Detail objednávky najdete na: {{action_url}}"). Anchoring on "the
    /// block's last line ends with a URL" covers both, and it picks the
    /// right one in the shipped-order body, where <c>tracking_url</c>
    /// sits on a line in the MIDDLE of the block and must stay an inline
    /// link rather than steal the button from <c>action_url</c>.
    /// </para>
    /// </summary>
    private static string? TrailingUrl(string block)
    {
        var lastLine = block.Split('\n')[^1].TrimEnd();
        var lastSpace = lastLine.LastIndexOf(' ');
        var candidate = lastSpace < 0 ? lastLine : lastLine[(lastSpace + 1)..];
        return IsSafeHttpUrl(candidate) ? candidate : null;
    }

    /// <summary>A block that is nothing but one absolute http(s) URL.</summary>
    private static bool IsUrlOnly(string block)
    {
        var candidate = block.Trim();
        return !candidate.Contains('\n') && !candidate.Contains(' ') && IsSafeHttpUrl(candidate);
    }

    private static bool IsSafeHttpUrl(string candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string BuildPreheader(IReadOnlyList<string> blocks)
    {
        // The first prose block, flattened. Shown by most clients next to
        // the subject in the inbox list; without one they leak whatever
        // markup-adjacent text comes first.
        var first = blocks.FirstOrDefault(b => !IsUrlOnly(b)) ?? string.Empty;
        var flat = first.Replace('\n', ' ').Trim();
        return flat.Length <= MaxPreheaderLength ? flat : flat[..MaxPreheaderLength].TrimEnd() + "…";
    }

    private static string DefaultCtaLabel(bool isEnglish) => isEnglish ? "Open the link" : "Otevřít odkaz";

    // === Fragments =======================================================

    private static void AppendParagraph(StringBuilder body, string block, bool muted)
    {
        var color = muted ? EmailTheme.InkMuted : EmailTheme.InkBody;
        var size = muted ? "14px" : "15px";
        var html = EncodeWithLinks(block).Replace("\n", "<br />");

        body.Append("<p style=\"margin:0 0 16px;font-family:")
            .Append(EmailTheme.FontStack)
            .Append(";font-size:").Append(size)
            .Append(";line-height:1.65;color:").Append(color)
            .Append(";\">").Append(html).Append("</p>");
    }

    private static void AppendButton(StringBuilder body, string url, string label)
    {
        var href = Escape(url);
        body.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:24px 0 20px;\"><tr><td bgcolor=\"")
            .Append(EmailTheme.Brand)
            .Append("\" style=\"border-radius:6px;\"><a href=\"").Append(href)
            .Append("\" style=\"display:inline-block;padding:13px 28px;font-family:")
            .Append(EmailTheme.FontStack)
            .Append(";font-size:15px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:6px;\">")
            .Append(Escape(label))
            .Append("</a></td></tr></table>");
    }

    /// <summary>
    /// The "button didn't work" escape hatch. Corporate mail filters
    /// rewrite or strip anchors often enough that a one-time login link
    /// with no copyable form is a support ticket waiting to happen.
    /// </summary>
    private static void AppendFallbackLink(StringBuilder body, string url, bool isEnglish)
    {
        var intro = isEnglish
            ? "If the button doesn't work, paste this link into your browser:"
            : "Pokud tlačítko nefunguje, zkopírujte tento odkaz do prohlížeče:";
        var href = Escape(url);

        body.Append("<p style=\"margin:24px 0 8px;font-family:").Append(EmailTheme.FontStack)
            .Append(";font-size:13px;line-height:1.6;color:").Append(EmailTheme.InkMuted)
            .Append(";\">").Append(Escape(intro)).Append("</p>")
            .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\"><tr><td style=\"padding:12px 14px;background-color:")
            .Append(EmailTheme.BandFill)
            .Append(";border:1px solid ").Append(EmailTheme.HairlineSoft)
            .Append(";border-radius:6px;font-family:")
            .Append(EmailTheme.FontStack)
            .Append(";font-size:13px;line-height:1.5;color:").Append(EmailTheme.InkFaint)
            .Append(";word-break:break-all;\"><a href=\"").Append(href)
            .Append("\" style=\"color:").Append(EmailTheme.Brand)
            .Append(";text-decoration:none;word-break:break-all;\">").Append(href)
            .Append("</a></td></tr></table>");
    }

    /// <summary>
    /// Escape a text block for an HTML context, promoting any bare
    /// http(s) URL inside it to an anchor on the way.
    ///
    /// <para>
    /// The URL scan runs over the RAW text and every non-URL run is
    /// escaped as it is emitted, so there is no window in which
    /// attacker-controlled markup sits unescaped in the output buffer.
    /// A token that isn't a clean absolute http(s) URI is emitted as
    /// escaped text — a <c>javascript:</c> or <c>data:</c> "link" never
    /// becomes an href.
    /// </para>
    /// </summary>
    private static string EncodeWithLinks(string block)
    {
        var result = new StringBuilder(block.Length + 32);
        var i = 0;

        while (i < block.Length)
        {
            var start = block.IndexOf("http", i, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(Escape(block[i..]));
                break;
            }

            var end = start;
            while (end < block.Length && !char.IsWhiteSpace(block[end])) end++;

            // Sentence punctuation clings to a URL in prose; it is not part
            // of the link.
            var token = block[start..end].TrimEnd('.', ',', ';', ')');

            if (!IsSafeHttpUrl(token))
            {
                result.Append(Escape(block[i..end]));
                i = end;
                continue;
            }

            var href = Escape(token);
            result.Append(Escape(block[i..start]))
                  .Append("<a href=\"").Append(href).Append("\" style=\"color:")
                  .Append(EmailTheme.Brand).Append(";text-decoration:underline;\">")
                  .Append(href).Append("</a>")
                  .Append(Escape(block[(start + token.Length)..end]));
            i = end;
        }

        return result.ToString();
    }

    /// <summary>
    /// Escape the five characters that matter in an HTML text or
    /// double-quoted attribute context.
    ///
    /// <para>
    /// Deliberately NOT <see cref="System.Net.WebUtility.HtmlEncode"/>: that also
    /// turns every non-ASCII character into a numeric entity, which for
    /// Czech copy means most of the body ships as <c>&amp;#345;</c> noise
    /// — larger on the wire, unreadable in a "view source", and pointless
    /// when the document declares UTF-8.
    /// </para>
    /// </summary>
    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // === Shell ===========================================================

    private static string Shell(string heading, string content, string preheader, bool isEnglish)
    {
        var footerNote = isEnglish
            ? "This message was sent automatically from an unmonitored address — please don't reply to it."
            : "Tuto zprávu odeslal automat z adresy, která nepřijímá odpovědi — neodpovídejte na ni prosím.";
        var tagline = isEnglish ? "Where Ideas Take Shape." : "Where Ideas Take Shape.";

        var sb = new StringBuilder(4096);

        sb.Append("<!DOCTYPE html><html lang=\"")
          .Append(isEnglish ? "en" : "cs")
          .Append("\"><head><meta charset=\"utf-8\" />")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\" />")
          .Append("<meta name=\"x-apple-disable-message-reformatting\" />")
          // Declaring one scheme is what keeps Gmail/Outlook dark mode from
          // inverting the palette into something we never designed.
          .Append("<meta name=\"color-scheme\" content=\"light\" />")
          .Append("<meta name=\"supported-color-schemes\" content=\"light\" />")
          .Append("<title>").Append(Escape(heading)).Append("</title>")
          .Append("<style>@media (max-width:620px){.mk-card{padding:24px 20px !important;}.mk-shell{padding:16px 12px !important;}}</style>")
          .Append("</head>");

        sb.Append("<body style=\"margin:0;padding:0;background-color:").Append(EmailTheme.BandFill)
          .Append(";color-scheme:light;\">");

        // Preheader: the inbox preview line. Hidden in the body itself,
        // then padded so the client doesn't pull real copy in after it.
        sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;\">")
          .Append(Escape(preheader))
          .Append(string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 30)))
          .Append("</div>");

        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"background-color:")
          .Append(EmailTheme.BandFill).Append(";\"><tr><td align=\"center\" class=\"mk-shell\" style=\"padding:32px 16px;\">")
          .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"600\" style=\"width:100%;max-width:600px;\">");

        // Masthead: wordmark + tagline over a single brand hairline. No
        // image — a logo that needs "display remote content" is a logo
        // most recipients never see.
        sb.Append("<tr><td style=\"padding:0 0 20px;\">")
          .Append("<div style=\"font-family:").Append(EmailTheme.FontStack)
          .Append(";font-size:17px;font-weight:700;letter-spacing:0.16em;text-transform:uppercase;color:")
          .Append(EmailTheme.Brand).Append(";\">Makables</div>")
          .Append("<div style=\"margin-top:4px;font-family:").Append(EmailTheme.FontStack)
          .Append(";font-size:12px;letter-spacing:0.04em;color:").Append(EmailTheme.InkFaint)
          .Append(";\">").Append(Escape(tagline)).Append("</div>")
          .Append("</td></tr>")
          .Append("<tr><td style=\"font-size:0;line-height:0;height:2px;background-color:")
          .Append(EmailTheme.BrandLine).Append(";\">&nbsp;</td></tr>");

        // The card.
        sb.Append("<tr><td class=\"mk-card\" style=\"padding:32px 32px 28px;background-color:")
          .Append(EmailTheme.Paper).Append(";border:1px solid ").Append(EmailTheme.Hairline)
          .Append(";border-top:0;border-radius:0 0 8px 8px;\">")
          .Append("<h1 style=\"margin:0 0 18px;font-family:").Append(EmailTheme.FontStack)
          .Append(";font-size:21px;line-height:1.3;font-weight:650;color:").Append(EmailTheme.InkTitle)
          .Append(";\">").Append(Escape(heading)).Append("</h1>")
          .Append(content)
          .Append("</td></tr>");

        // Footer.
        sb.Append("<tr><td style=\"padding:20px 4px 0;font-family:").Append(EmailTheme.FontStack)
          .Append(";font-size:12px;line-height:1.6;color:").Append(EmailTheme.InkFaint).Append(";\">")
          .Append(Escape(footerNote))
          .Append("<br />").Append(OperatorLine)
          .Append(" · <a href=\"https://makables.cz\" style=\"color:").Append(EmailTheme.InkMuted)
          .Append(";text-decoration:underline;\">makables.cz</a>")
          .Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }
}

/// <summary>
/// Button labels for the action link. This is chrome, not copy — the
/// prose all lives in <c>email_template_translations</c>; a button label
/// is a verb attached to a URL the template body doesn't name. Kept to
/// one short string per audience-facing template so a missing entry
/// degrades to a truthful generic rather than an empty button.
/// </summary>
public static class EmailCallToAction
{
    public static string? LabelFor(EmailTemplateType type, string languageCode)
    {
        var en = string.Equals(languageCode, LanguageCode.EnUS, StringComparison.OrdinalIgnoreCase);

        return type switch
        {
            EmailTemplateType.AuthMagicLink => en ? "Sign in" : "Přihlásit se",
            EmailTemplateType.AuthEmailConfirmation => en ? "Confirm email" : "Potvrdit e-mail",
            EmailTemplateType.AuthPasswordReset => en ? "Set a new password" : "Nastavit nové heslo",

            EmailTemplateType.OrderPaidCustomer
                or EmailTemplateType.OrderAcceptedCustomer
                or EmailTemplateType.OrderShippedCustomer
                or EmailTemplateType.OrderDeliveredCustomer
                or EmailTemplateType.OrderCancelledAutoExpiryCustomer
                or EmailTemplateType.OrderRefundedCustomer
                or EmailTemplateType.OrderDisputeResolvedCustomer
                or EmailTemplateType.OrderPlacedMaker
                    => en ? "View the order" : "Zobrazit objednávku",

            EmailTemplateType.OrderMessagePostedCustomer
                or EmailTemplateType.OrderMessagePostedMaker
                    => en ? "Read the message" : "Přečíst zprávu",

            EmailTemplateType.OrderDisputedAdmin
                or EmailTemplateType.DisputeAutoEscalatedAdmin
                    => en ? "Open the dispute" : "Otevřít reklamaci",

            EmailTemplateType.PayoutFeeInvoiceMaker => en ? "View the invoice" : "Zobrazit fakturu",
            EmailTemplateType.PayoutSentMaker => en ? "View the payout" : "Zobrazit výplatu",

            _ => null,
        };
    }
}
