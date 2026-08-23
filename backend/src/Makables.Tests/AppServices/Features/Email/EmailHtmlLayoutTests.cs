using FluentAssertions;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Email;

namespace Makables.Tests.AppServices.Features.Email;

/// <summary>
/// Pins the HTML part composed for every transactional email.
///
/// <para>
/// The layout derives its content from the already-substituted plain-text
/// translation, so the two tests that matter are: the derivation is
/// faithful (nothing dropped, the action URL becomes a real button), and
/// the derivation is safe (a body can carry user-supplied free text —
/// dispute descriptions, admin resolution notes — that never had to
/// survive an HTML context before).
/// </para>
/// </summary>
public class EmailHtmlLayoutTests
{
    private const string Url = "https://makables.cz/reset?token=abc123";

    private const string ResetBody =
        "Dostali jsme žádost o nastavení nového hesla k vašemu účtu Makables. Pokračujte přes odkaz níže.\n\n"
        + Url + "\n\n"
        + "Odkaz je platný do 24. 8. 2026, 20:30.\n\n"
        + "Pokud jste o obnovení hesla nežádali, e-mail ignorujte — vaše stávající heslo zůstává v platnosti.\n\n"
        + "Makables — makables.cz";

    private static string RenderReset() =>
        EmailHtmlLayout.Render("Obnovení hesla", ResetBody, "Nastavit nové heslo", "cs-CZ");

    // === Structure ===

    [Fact]
    public void Renders_a_complete_html_document()
    {
        var html = RenderReset();

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().EndWith("</html>");
        html.Should().Contain("<meta charset=\"utf-8\" />");
    }

    [Fact]
    public void Uses_the_subject_as_the_heading()
    {
        RenderReset().Should().Contain(">Obnovení hesla</h1>");
    }

    [Fact]
    public void Promotes_the_standalone_url_to_a_labelled_button()
    {
        var html = RenderReset();

        html.Should().Contain($"<a href=\"{Url}\"");
        html.Should().Contain(">Nastavit nové heslo</a>");
    }

    [Fact]
    public void Falls_back_to_a_generic_button_label_when_none_is_mapped()
    {
        var html = EmailHtmlLayout.Render("Něco", ResetBody, ctaLabel: null, languageCode: "cs-CZ");

        html.Should().Contain(">Otevřít odkaz</a>");
    }

    [Fact]
    public void Keeps_a_copyable_link_for_clients_that_mangle_anchors()
    {
        // Corporate filters rewrite or strip anchors; a one-time token with
        // no copyable form is a support ticket.
        var html = RenderReset();

        html.Should().Contain("zkopírujte tento odkaz");
        html.Should().Contain($">{Url}</a>");
    }

    [Fact]
    public void Copyable_link_sits_after_the_small_print_not_between_the_button_and_it()
    {
        var html = RenderReset();

        html.IndexOf("Odkaz je platný do", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("zkopírujte tento odkaz", StringComparison.Ordinal));
    }

    [Fact]
    public void Carries_every_prose_block_from_the_plain_text()
    {
        var html = RenderReset();

        html.Should().Contain("Dostali jsme žádost o nastavení nového hesla");
        html.Should().Contain("Odkaz je platný do 24. 8. 2026, 20:30.");
        html.Should().Contain("vaše stávající heslo zůstává v platnosti");
    }

    [Fact]
    public void Drops_the_plain_text_sign_off_because_the_footer_already_carries_it()
    {
        var html = RenderReset();

        // "Makables — makables.cz" printed twice reads as a templating bug.
        html.Split("makables.cz").Length.Should().BeLessThanOrEqualTo(
            // masthead link + footer link + the action URL's host occurrences
            html.Split("Makables — makables.cz").Length + 6);
        html.Should().NotContain("Makables — makables.cz");
    }

    [Fact]
    public void Names_the_no_reply_nature_of_the_sender()
    {
        RenderReset().Should().Contain("neodpovídejte");
    }

    [Fact]
    public void Declares_a_single_colour_scheme_so_dark_mode_clients_do_not_invert_it()
    {
        var html = RenderReset();

        html.Should().Contain("<meta name=\"color-scheme\" content=\"light\" />");
        html.Should().Contain("<meta name=\"supported-color-schemes\" content=\"light\" />");
    }

    [Fact]
    public void Leaves_no_unsubstituted_placeholder_behind()
    {
        RenderReset().Should().NotContain("{{");
    }

    // === Language ===

    [Fact]
    public void English_bodies_get_English_chrome()
    {
        var html = EmailHtmlLayout.Render(
            "Password reset",
            "We received a request.\n\n" + Url + "\n\nThe link is valid until 24 Aug.",
            EmailCallToAction.LabelFor(EmailTemplateType.AuthPasswordReset, "en-US"),
            "en-US");

        html.Should().Contain("<html lang=\"en\"");
        html.Should().Contain(">Set a new password</a>");
        // The apostrophe is escaped — attribute-safe escaping applies to
        // text nodes too, and &#39; renders as ' in every client.
        html.Should().Contain("If the button doesn&#39;t work");
        html.Should().NotContain("zkopírujte");
    }

    [Fact]
    public void Czech_is_the_default_for_anything_that_is_not_English()
    {
        EmailHtmlLayout.Render("X", ResetBody, null, "cs-CZ")
            .Should().Contain("<html lang=\"cs\"");
    }

    // === Security ===

    [Fact]
    public void Escapes_markup_in_user_supplied_free_text()
    {
        // A dispute description is customer-authored and lands in the body
        // verbatim. The plain-text pipeline never had to care.
        var body = "Zákazník napsal:\n\n<script>alert('xss')</script>\n\n" + Url;

        var html = EmailHtmlLayout.Render("Nová reklamace", body, "Otevřít reklamaci", "cs-CZ");

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Escapes_markup_smuggled_through_the_heading()
    {
        var html = EmailHtmlLayout.Render(
            "<img src=x onerror=alert(1)>", ResetBody, "Otevřít", "cs-CZ");

        html.Should().NotContain("<img src=x");
        html.Should().Contain("&lt;img src=x");
    }

    [Fact]
    public void Never_turns_a_javascript_url_into_an_href()
    {
        var body = "Klikněte:\n\njavascript:alert(document.cookie)\n\nKonec.";

        var html = EmailHtmlLayout.Render("Test", body, "Otevřít", "cs-CZ");

        html.Should().NotContain("href=\"javascript:");
        html.Should().Contain("javascript:alert(document.cookie)");
    }

    [Fact]
    public void Never_turns_a_data_url_into_an_href()
    {
        var body = "Odkaz:\n\ndata:text/html;base64,PHNjcmlwdD4=\n\nKonec.";

        var html = EmailHtmlLayout.Render("Test", body, "Otevřít", "cs-CZ");

        html.Should().NotContain("href=\"data:");
    }

    [Fact]
    public void Linkifies_a_bare_url_embedded_in_a_sentence()
    {
        var body = "Sledujte zásilku na https://tracking.packeta.cz/Z123 kdykoliv.";

        var html = EmailHtmlLayout.Render("Odesláno", body, null, "cs-CZ");

        html.Should().Contain("<a href=\"https://tracking.packeta.cz/Z123\"");
        html.Should().Contain("kdykoliv.");
    }

    [Fact]
    public void An_attacker_controlled_lookalike_url_cannot_break_out_of_the_href_attribute()
    {
        var body = "Text: https://evil.example/\"onmouseover=\"alert(1) konec.";

        var html = EmailHtmlLayout.Render("Test", body, null, "cs-CZ");

        html.Should().NotContain("onmouseover=\"alert(1)\"");
        html.Should().NotContain("\"onmouseover");
    }

    // === Degenerate input ===

    [Fact]
    public void A_body_with_no_url_renders_without_a_button()
    {
        var html = EmailHtmlLayout.Render("Bez odkazu", "Jen text.\n\nA ještě řádek.", "Otevřít", "cs-CZ");

        html.Should().Contain("Jen text.");
        html.Should().NotContain(">Otevřít</a>");
        html.Should().NotContain("zkopírujte tento odkaz");
    }

    [Fact]
    public void An_empty_body_still_produces_a_valid_document()
    {
        var html = EmailHtmlLayout.Render("Předmět", string.Empty, null, "cs-CZ");

        html.Should().StartWith("<!DOCTYPE html>").And.EndWith("</html>");
        html.Should().Contain(">Předmět</h1>");
    }

    [Fact]
    public void Single_newlines_inside_a_block_become_line_breaks_not_lost_text()
    {
        var html = EmailHtmlLayout.Render("X", "Řádek jedna\nŘádek dva", null, "cs-CZ");

        html.Should().Contain("Řádek jedna<br />Řádek dva");
    }

    // === Copy shapes in the real catalog ===

    [Fact]
    public void A_url_ending_a_sentence_still_becomes_the_button()
    {
        // The order / payout templates never put the link on its own line.
        var body = "Dobrý den Anna,\n\n"
            + "děkujeme za objednávku M-2026-0042.\n"
            + "Detail objednávky najdete na: https://makables.cz/objednavka/ord-42\n\n"
            + "Makables — makables.cz";

        var html = EmailHtmlLayout.Render("Děkujeme", body, "Zobrazit objednávku", "cs-CZ");

        html.Should().Contain("<a href=\"https://makables.cz/objednavka/ord-42\"");
        html.Should().Contain(">Zobrazit objednávku</a>");
        // The lead-in survives as prose above the button.
        html.Should().Contain("Detail objednávky najdete na:");
        html.Should().NotContain("na: <a");
    }

    [Fact]
    public void A_mid_block_url_does_not_steal_the_button_from_the_action_url()
    {
        // The shipped-order body carries {{tracking_url}} on its own line
        // in the MIDDLE, then ends with the action URL.
        var body = "objednávka M-2026-0042 byla odeslána.\n"
            + "https://tracking.packeta.cz/Z123\n"
            + "Detail najdete na: https://makables.cz/objednavka/ord-42";

        var html = EmailHtmlLayout.Render("Odesláno", body, "Zobrazit objednávku", "cs-CZ");

        // The button is the order detail…
        html.Should().Contain(
            "<a href=\"https://makables.cz/objednavka/ord-42\" style=\"display:inline-block");
        // …and the tracking URL stays an inline link, not a second button.
        html.Should().Contain("<a href=\"https://tracking.packeta.cz/Z123\" style=\"color:");
    }

    // === Call-to-action catalog ===

    [Theory]
    [InlineData(EmailTemplateType.AuthMagicLink, "Přihlásit se")]
    [InlineData(EmailTemplateType.AuthEmailConfirmation, "Potvrdit e-mail")]
    [InlineData(EmailTemplateType.AuthPasswordReset, "Nastavit nové heslo")]
    [InlineData(EmailTemplateType.OrderPaidCustomer, "Zobrazit objednávku")]
    [InlineData(EmailTemplateType.PayoutSentMaker, "Zobrazit výplatu")]
    public void Maps_a_Czech_button_label_per_template_type(EmailTemplateType type, string expected) =>
        EmailCallToAction.LabelFor(type, "cs-CZ").Should().Be(expected);

    [Fact]
    public void Every_shipped_template_type_has_a_button_label_in_both_languages()
    {
        // A missing entry degrades to a generic label rather than an empty
        // button, but the catalog should still be complete — a new type
        // added without a label is what this pins.
        foreach (var type in Enum.GetValues<EmailTemplateType>())
        {
            EmailCallToAction.LabelFor(type, "cs-CZ").Should().NotBeNullOrWhiteSpace(
                because: $"{type} needs a Czech call-to-action label");
            EmailCallToAction.LabelFor(type, "en-US").Should().NotBeNullOrWhiteSpace(
                because: $"{type} needs an English call-to-action label");
        }
    }
}
