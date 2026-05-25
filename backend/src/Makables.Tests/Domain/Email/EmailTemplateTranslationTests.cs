using FluentAssertions;
using Makables.Core.Domain.Email;

namespace Makables.Tests.Domain.Email;

public class EmailTemplateTranslationTests
{
    [Fact]
    public void Create_trims_subject_and_stores_body_verbatim()
    {
        var t = EmailTemplateTranslation.Create(
            id: "tr-1",
            emailTemplateId: "tpl-1",
            languageCode: "cs-CZ",
            subject: "  Přihlášení  ",
            plainTextBody: "Klikněte: {{action_url}}",
            countryCode: "cz");

        t.Subject.Should().Be("Přihlášení");
        t.PlainTextBody.Should().Be("Klikněte: {{action_url}}");
        t.LanguageCode.Should().Be("cs-CZ");
        t.CountryCode.Should().Be("CZ");
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("CS-cz")]
    public void Create_rejects_malformed_language_code(string lang)
    {
        var act = () => EmailTemplateTranslation.Create(
            "tr-1", "tpl-1", lang, "Subject", "Body", "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_swaps_subject_and_body()
    {
        var t = EmailTemplateTranslation.Create(
            "tr-1", "tpl-1", "en-US", "Old", "Old body", "CZ");

        t.Update("New subject", "New body");

        t.Subject.Should().Be("New subject");
        t.PlainTextBody.Should().Be("New body");
    }
}
