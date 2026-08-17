using FluentAssertions;
using Makables.Infra.Clients.Ares.Mapping;

namespace Makables.Tests.Infra.Clients.Ares;

/// <summary>
/// Pins <see cref="AresResponseMapper.TryMap"/>, the single seam both
/// registration paths cross (maker T-0033 and customer-company T-0162).
///
/// <para>
/// T-0163 (secops F-1/F-2 on the T-0162 review) added the company-name
/// leg: ARES's <c>obchodniJmeno</c> is free registry text that lands in a
/// <c>varchar(300)</c> column on both <c>makers</c> and <c>users</c>, and
/// an absent name used to map to <see cref="string.Empty"/>. Neither was
/// caught here before — an oversized name reached Postgres as a 22001 and
/// an empty one reached <c>Maker.Create</c>'s <c>ArgumentException</c>, so
/// both surfaced as a 500 on a *user-triggered* path instead of the
/// Permanent business error every other structural defect gets.
/// </para>
///
/// The rest of the cases are characterization tests pinning the T-0032
/// behaviour the hardening must not disturb.
/// </summary>
public class AresResponseMapperTests
{
    private const string ValidIco = "27074358";

    private static AresEkonomickySubjekt Payload(
        string? ico = ValidIco,
        string? obchodniJmeno = "Avast Software s.r.o.",
        string? dic = "CZ27074358",
        string? pravniForma = "112",
        string? datumVzniku = "2006-09-04",
        string? datumZaniku = null,
        AresSidlo? sidlo = null) =>
        new(
            Ico: ico,
            ObchodniJmeno: obchodniJmeno,
            Dic: dic,
            PravniForma: pravniForma,
            DatumVzniku: datumVzniku,
            DatumZaniku: datumZaniku,
            Sidlo: sidlo ?? new AresSidlo(
                NazevUlice: "Pikrtova",
                CisloDomovni: 1737,
                NazevObce: "Praha",
                Psc: 14000,
                NazevStatu: "Česká republika"));

    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

    // ---- T-0163 F-2: company name is required ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Missing_company_name_is_a_permanent_map_failure(string? obchodniJmeno)
    {
        var record = AresResponseMapper.TryMap(
            Payload(obchodniJmeno: obchodniJmeno), Now, out var failure);

        record.Should().BeNull();
        failure.Should().Be(MapFailure.MissingCompanyName);
    }

    // ---- T-0163 F-1: oversized company name is capped ----

    [Fact]
    public void Oversized_company_name_is_capped_to_the_column_width()
    {
        var oversized = new string('A', AresResponseMapper.MaxCompanyNameLength + 250);

        var record = AresResponseMapper.TryMap(
            Payload(obchodniJmeno: oversized), Now, out var failure);

        failure.Should().Be(MapFailure.None);
        record!.CompanyName.Length.Should().Be(AresResponseMapper.MaxCompanyNameLength);
        record.CompanyName.Should().Be(new string('A', AresResponseMapper.MaxCompanyNameLength));
    }

    [Fact]
    public void Company_name_exactly_at_the_cap_survives_untouched()
    {
        var exact = new string('B', AresResponseMapper.MaxCompanyNameLength);

        var record = AresResponseMapper.TryMap(Payload(obchodniJmeno: exact), Now, out _);

        record!.CompanyName.Should().Be(exact);
    }

    [Fact]
    public void Capping_trims_the_whitespace_the_cut_may_expose()
    {
        // A cut mid-name can leave a trailing space; the stored snapshot is
        // display copy, so it must not end in one.
        var name = new string('C', AresResponseMapper.MaxCompanyNameLength - 1) + "   tail";

        var record = AresResponseMapper.TryMap(Payload(obchodniJmeno: name), Now, out _);

        record!.CompanyName.Should().Be(new string('C', AresResponseMapper.MaxCompanyNameLength - 1));
        record.CompanyName.Should().NotEndWith(" ");
    }

    [Fact]
    public void Company_name_is_trimmed()
    {
        var record = AresResponseMapper.TryMap(
            Payload(obchodniJmeno: "  Avast Software s.r.o.  "), Now, out _);

        record!.CompanyName.Should().Be("Avast Software s.r.o.");
    }

    // ---- characterization: T-0032 behaviour the hardening must preserve ----

    [Fact]
    public void Valid_payload_maps_every_field()
    {
        var record = AresResponseMapper.TryMap(Payload(), Now, out var failure);

        failure.Should().Be(MapFailure.None);
        record.Should().NotBeNull();
        record!.RegistrationNumber.Should().Be(ValidIco);
        record.VatId.Should().Be("CZ27074358");
        record.CompanyName.Should().Be("Avast Software s.r.o.");
        record.IncorporatedOn.Should().Be(new DateOnly(2006, 9, 4));
        record.IsActiveInRegistry.Should().BeTrue();
        record.SourceRegistry.Should().Be("ares");
        record.FetchedAt.Should().Be(Now);
        record.IsStale.Should().BeFalse();
        record.RegisteredAddress.Street.Should().Be("Pikrtova");
        record.RegisteredAddress.HouseNumber.Should().Be("1737");
        record.RegisteredAddress.City.Should().Be("Praha");
        record.RegisteredAddress.Zip.Should().Be("14000");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_ico_is_a_map_failure(string? ico)
    {
        var record = AresResponseMapper.TryMap(Payload(ico: ico), Now, out var failure);

        record.Should().BeNull();
        failure.Should().Be(MapFailure.MissingIco);
    }

    [Fact]
    public void Missing_sidlo_is_an_incomplete_sidlo_failure()
    {
        // Constructed inline rather than through Payload(): the helper
        // substitutes a valid sidlo for a null argument.
        var noSidlo = new AresEkonomickySubjekt(
            ValidIco, "Avast Software s.r.o.", null, "112", null, null, Sidlo: null);

        var record = AresResponseMapper.TryMap(noSidlo, Now, out var failure);

        record.Should().BeNull();
        failure.Should().Be(MapFailure.IncompleteSidlo);
    }

    [Theory]
    [InlineData(null, 1737, 14000)]   // no city
    [InlineData("Praha", 1737, null)] // no ZIP
    public void Sidlo_missing_city_or_zip_is_an_incomplete_sidlo_failure(
        string? city, int? houseNumber, int? zip)
    {
        var record = AresResponseMapper.TryMap(
            Payload(sidlo: new AresSidlo("Pikrtova", houseNumber, city, zip, "Česká republika")),
            Now,
            out var failure);

        record.Should().BeNull();
        failure.Should().Be(MapFailure.IncompleteSidlo);
    }

    [Fact]
    public void Sidlo_without_street_or_house_number_is_an_incomplete_sidlo_failure()
    {
        var record = AresResponseMapper.TryMap(
            Payload(sidlo: new AresSidlo(null, null, "Praha", 14000, "Česká republika")),
            Now,
            out var failure);

        record.Should().BeNull();
        failure.Should().Be(MapFailure.IncompleteSidlo);
    }

    [Fact]
    public void Street_falls_back_to_the_city_name_when_ares_omits_it()
    {
        // Small villages and home-based OSVČ have no street label in ARES.
        var record = AresResponseMapper.TryMap(
            Payload(sidlo: new AresSidlo(null, 42, "Lhota", 25101, "Česká republika")),
            Now,
            out _);

        record!.RegisteredAddress.Street.Should().Be("Lhota");
        record.RegisteredAddress.HouseNumber.Should().Be("42");
    }

    [Fact]
    public void Dissolved_company_maps_with_IsActiveInRegistry_false()
    {
        var record = AresResponseMapper.TryMap(
            Payload(datumZaniku: "2024-01-31"), Now, out _);

        record!.IsActiveInRegistry.Should().BeFalse();
    }

    [Fact]
    public void Unparseable_incorporation_date_maps_as_null_not_a_failure()
    {
        var record = AresResponseMapper.TryMap(
            Payload(datumVzniku: "not-a-date"), Now, out var failure);

        failure.Should().Be(MapFailure.None);
        record!.IncorporatedOn.Should().BeNull();
    }

    [Fact]
    public void Blank_dic_maps_to_null()
    {
        var record = AresResponseMapper.TryMap(Payload(dic: "  "), Now, out _);

        record!.VatId.Should().BeNull();
    }
}
