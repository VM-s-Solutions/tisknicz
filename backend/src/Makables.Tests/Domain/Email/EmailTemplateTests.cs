using FluentAssertions;
using Makables.Core.Domain.Email;

namespace Makables.Tests.Domain.Email;

public class EmailTemplateTests
{
    [Fact]
    public void Create_normalizes_country_code_to_uppercase_and_strips_blank_overrides()
    {
        var t = EmailTemplate.Create(
            id: "tpl-1",
            type: EmailTemplateType.AuthMagicLink,
            providerTemplateId: "d-abc",
            countryCode: "cz",
            fromAddress: "  ",
            fromName: "",
            replyToAddress: null);

        t.CountryCode.Should().Be("CZ");
        t.ProviderTemplateId.Should().Be("d-abc");
        t.FromAddress.Should().BeNull("whitespace from address is normalized to null");
        t.FromName.Should().BeNull();
        t.ReplyToAddress.Should().BeNull();
    }

    [Fact]
    public void UpdateProviderTemplateId_swaps_in_a_new_id()
    {
        var t = EmailTemplate.Create("tpl-1", EmailTemplateType.AuthMagicLink,
            "d-old", "CZ");

        t.UpdateProviderTemplateId("d-new");

        t.ProviderTemplateId.Should().Be("d-new");
    }

    [Fact]
    public void Create_rejects_empty_providerTemplateId()
    {
        var act = () => EmailTemplate.Create("tpl-1", EmailTemplateType.AuthMagicLink, " ", "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_malformed_countryCode()
    {
        var act = () => EmailTemplate.Create("tpl-1", EmailTemplateType.AuthMagicLink, "d-abc", "CZE");
        act.Should().Throw<ArgumentException>();
    }
}
