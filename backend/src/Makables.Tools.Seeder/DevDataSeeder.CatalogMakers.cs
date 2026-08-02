using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Makables.Tools.Seeder;

/// <summary>
/// Pass 2 of the dev seed: 50 catalog makers, enough to exercise paging,
/// the city / category / rating filters and the sort order on
/// <c>/katalog</c> with data that reads like the real thing.
///
/// <para>
/// <b>Idempotency is per maker, not per run.</b> The demo snapshot in
/// <c>DevDataSeeder.cs</c> is guarded by one sentinel row because it is a
/// single indivisible dataset; this list is not. Dev databases already
/// hold data (the snapshot, rows made by hand through the UI, an earlier
/// and shorter version of this list), so every blueprint is checked
/// against the database before it is built, and one whose maker id,
/// slug, IČO, user id or e-mail is already taken is left alone. Adding
/// blueprint 51 later and redeploying therefore inserts exactly that one
/// maker.
/// </para>
///
/// <para>
/// Everything a blueprint owns hangs off its 1-based position:
/// <c>seed-cmaker-07</c>, <c>seed-user-cmaker-07</c>,
/// <c>seed-cprod-07-1</c>, <c>seed-creview-07-1</c>, order numbers
/// <c>M-CZ-{YYYY}9148…</c>. So a cluster is created or skipped as a
/// whole, and a re-run can never half-build one.
/// </para>
/// </summary>
public sealed partial class DevDataSeeder
{
    /// <summary>
    /// Order numbers for this pass start here, above the demo snapshot's
    /// 9001–9014 and still inside the reserved <c>M-CZ-{YYYY}9NNN</c>
    /// range that the live <c>IOrderNumberGenerator</c> never reaches.
    /// </summary>
    private const int CatalogOrderNumberBase = 9_100;

    /// <summary>
    /// Order-number stride per maker. Fifty makers × 8 tops out at 9499,
    /// comfortably inside the 9xxx range.
    /// </summary>
    private const int CatalogOrdersPerMaker = 8;

    private const string CatalogEmailDomain = "@makables.test";

    /// <summary>
    /// Builds every blueprint that isn't in the database yet.
    /// Returns how many were added and how many were already there.
    /// </summary>
    private async Task<(int Added, int Skipped)> SeedCatalogMakersAsync(CancellationToken ct)
    {
        var plans = BuildCatalogPlans();

        var makerIds = plans.Select(p => p.MakerId).ToList();
        var slugs = plans.Select(p => p.Blueprint.Slug).ToList();
        var icos = plans.Select(p => p.Ico).ToList();
        var userIds = plans.Select(p => p.UserId).ToList();
        var emails = plans.Select(p => p.EmailNormalized).ToList();

        // Query filters are OFF on purpose: a soft-deleted maker still
        // owns its id, and a dev tester who deactivated one did not ask
        // for it back.
        var takenMakers = await db.Set<Maker>()
            .IgnoreQueryFilters()
            .Where(m => makerIds.Contains(m.Id)
                || slugs.Contains(m.Slug)
                || icos.Contains(m.RegistrationNumber))
            .Select(m => new { m.Id, m.Slug, m.RegistrationNumber })
            .ToListAsync(ct);

        var takenUsers = await db.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id) || emails.Contains(u.EmailNormalized))
            .Select(u => new { u.Id, u.EmailNormalized })
            .ToListAsync(ct);

        var takenMakerIds = takenMakers.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var takenSlugs = takenMakers.Select(m => m.Slug).ToHashSet(StringComparer.Ordinal);
        var takenIcos = takenMakers.Select(m => m.RegistrationNumber).ToHashSet(StringComparer.Ordinal);
        var takenUserIds = takenUsers.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var takenEmails = takenUsers.Select(u => u.EmailNormalized).ToHashSet(StringComparer.Ordinal);

        var pending = plans.Where(p =>
            !takenMakerIds.Contains(p.MakerId)
            && !takenSlugs.Contains(p.Blueprint.Slug)
            && !takenIcos.Contains(p.Ico)
            && !takenUserIds.Contains(p.UserId)
            && !takenEmails.Contains(p.EmailNormalized)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("All {Total} catalog makers already present — nothing to add.", plans.Count);
            return (0, plans.Count);
        }

        var customers = await EnsureCatalogCustomersAsync(ct);
        foreach (var plan in pending)
        {
            BuildCatalogMaker(plan, customers);
        }

        logger.LogInformation(
            "Catalog makers: adding {Added} of {Total} ({Skipped} already present).",
            pending.Count, plans.Count, plans.Count - pending.Count);

        return (pending.Count, plans.Count - pending.Count);
    }

    private static List<CatalogMakerPlan> BuildCatalogPlans()
    {
        GuardBlueprintsAreUnique();

        return CatalogMakerBlueprints
            .Select((blueprint, i) =>
            {
                var index = i + 1;
                var email = CatalogEmail(blueprint.Owner);
                return new CatalogMakerPlan(
                    Index: index,
                    Blueprint: blueprint,
                    MakerId: $"seed-cmaker-{index:D2}",
                    UserId: $"seed-user-cmaker-{index:D2}",
                    Email: email,
                    EmailNormalized: User.NormalizeEmail(email),
                    Ico: CzechRegistrationNumber(index));
            })
            .ToList();
    }

    /// <summary>
    /// The shared customers the catalog orders are placed by. Reused
    /// across runs: an existing row (matched by id or e-mail) is taken as
    /// is rather than duplicated.
    /// </summary>
    private async Task<IReadOnlyList<User>> EnsureCatalogCustomersAsync(CancellationToken ct)
    {
        var ids = CatalogCustomers.Select((_, i) => $"seed-user-ccust-{i + 1:D2}").ToList();
        var emails = CatalogCustomers.Select(name => User.NormalizeEmail(CatalogEmail(name))).ToList();

        var existing = await db.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id) || emails.Contains(u.EmailNormalized))
            .ToListAsync(ct);

        var customers = new List<User>(CatalogCustomers.Length);
        for (var i = 0; i < CatalogCustomers.Length; i++)
        {
            var match = existing.FirstOrDefault(u =>
                string.Equals(u.Id, ids[i], StringComparison.Ordinal)
                || string.Equals(u.EmailNormalized, emails[i], StringComparison.Ordinal));

            customers.Add(match ?? AddUser(
                ids[i], CatalogEmail(CatalogCustomers[i]), UserRole.Customer,
                CatalogCustomers[i], PasswordHash, confirmed: true, DaysAgo(120)));
        }

        return customers;
    }

    private void BuildCatalogMaker(CatalogMakerPlan plan, IReadOnlyList<User> customers)
    {
        var blueprint = plan.Blueprint;
        var index = plan.Index;
        var createdAt = DaysAgo(95 + index * 4);

        var owner = AddUser(plan.UserId, plan.Email, UserRole.Maker, blueprint.Owner,
            PasswordHash, confirmed: true, createdAt);

        // Spread the optional profile fields deterministically so the
        // catalog shows every variant: unverified makers (no badge),
        // makers without a DIČ, makers offering personal pickup.
        var pickup = index % 3 == 0;

        // Every blueprint is named "… s.r.o.", so a živnostník seed takes
        // the OWNER's name as its registered name — that is what ARES
        // returns for an OSVČ, and it keeps the catalog from showing
        // "… s.r.o." rows filed under "Živnostník". Roughly one in three,
        // which leaves both filter buckets deep enough to page through.
        var legalType = index % 3 == 1 ? MakerLegalType.NaturalPerson : MakerLegalType.LegalEntity;
        var company = legalType == MakerLegalType.NaturalPerson ? blueprint.Owner : blueprint.Company;

        var maker = AddMaker(plan.MakerId, owner,
            ico: plan.Ico,
            vatId: index % 4 == 0 ? null : $"CZ{plan.Ico}",
            company: company,
            slug: blueprint.Slug,
            legalType: legalType,
            street: blueprint.Street,
            houseNumber: blueprint.HouseNumber,
            city: blueprint.City,
            zip: blueprint.Zip,
            bio: blueprint.Bio,
            bank: index % 13 == 0 ? null : $"{2_000_000_000 + index * 7_919}/{BankCodes[index % BankCodes.Length]}",
            pickup: pickup,
            pickupNote: pickup
                ? $"Osobní odběr po domluvě — {blueprint.Street} {blueprint.HouseNumber}, {blueprint.City}, všední dny 9–17 h."
                : null,
            verified: index % 11 != 0,
            createdAt: createdAt,
            categories: blueprint.Categories);

        var products = BuildCatalogProducts(plan, maker);
        BuildCatalogOrders(plan, maker, products, customers);
    }

    private List<Product> BuildCatalogProducts(CatalogMakerPlan plan, Maker maker)
    {
        var blueprint = plan.Blueprint;
        var index = plan.Index;
        // Well after the maker row (95+ days) and well before the first
        // order (52 days), so no order predates the product it is for.
        var createdAt = DaysAgo(75 + index % 10);
        var products = new List<Product>();

        // Two or three from the primary category — consecutive templates,
        // so no maker lists the same title twice.
        var primary = ProductTemplates[blueprint.Categories[0]];
        var primaryCount = 2 + index % 2;
        for (var i = 0; i < primaryCount; i++)
        {
            products.Add(AddCatalogProduct(plan, maker, blueprint.Categories[0],
                primary[(index + i) % primary.Length], products.Count + 1, createdAt.AddDays(i)));
        }

        if (blueprint.Categories.Length > 1)
        {
            var secondary = ProductTemplates[blueprint.Categories[1]];
            products.Add(AddCatalogProduct(plan, maker, blueprint.Categories[1],
                secondary[index % secondary.Length], products.Count + 1, createdAt.AddDays(primaryCount)));
        }

        return products;
    }

    private Product AddCatalogProduct(
        CatalogMakerPlan plan, Maker maker, string categoryId,
        ProductTemplate template, int slot, DateTimeOffset createdAt)
    {
        // Shift prices per maker so the catalog isn't fifty identical
        // price tags. OnRequest stays at 0 — the price is the whole point
        // of the enquiry.
        var priceMinor = template.PriceType == PriceType.OnRequest
            ? 0
            : template.PriceMinor + (plan.Index % 7) * 3_000;

        return AddProduct($"seed-cprod-{plan.Index:D2}-{slot}", maker, categoryId,
            template.Title, template.Description, priceMinor, template.PriceType,
            template.Fulfillment, template.WeightGrams, createdAt);
    }

    private void BuildCatalogOrders(
        CatalogMakerPlan plan, Maker maker,
        IReadOnlyList<Product> products, IReadOnlyList<User> customers)
    {
        var index = plan.Index;
        var reviewCount = CatalogReviewCounts[index % CatalogReviewCounts.Length];
        // More completed orders than reviews is the normal case — not
        // every customer writes one. Both stats are recomputed from these
        // rows in RecomputeMakerStats().
        var orderCount = Math.Min(reviewCount + index % 3, CatalogOrdersPerMaker);

        // An OnRequest product has no price yet, so it can't back an
        // order; every maker has at least two priced ones.
        var orderable = products.Where(p => p.PriceAmountMinor > 0).ToList();
        if (orderable.Count == 0 || orderCount == 0)
        {
            return;
        }

        for (var k = 0; k < orderCount; k++)
        {
            var customer = customers[(index + k) % customers.Count];
            var createdAt = DaysAgo(52 - k * 6);
            var order = AddOrder(
                CatalogOrderNumberBase + (index - 1) * CatalogOrdersPerMaker + k,
                customer, maker, orderable[k % orderable.Count], createdAt);

            RunToCompleted(order,
                paidAt: createdAt.AddHours(2),
                acceptedAt: createdAt.AddDays(1),
                shippedAt: createdAt.AddDays(2),
                carrierRef: $"Z{9_000_000_000L + index * 1_000 + k}",
                deliveredAt: createdAt.AddDays(4),
                completedAt: createdAt.AddDays(6));

            if (k >= reviewCount)
            {
                continue;
            }

            var (rating, body) = CatalogReviews[(index * 5 + k) % CatalogReviews.Length];
            var review = AddReview($"seed-creview-{index:D2}-{k + 1}", order, customer,
                rating, body, createdAt.AddDays(7));

            if ((index + k) % 4 == 0)
            {
                review.AddReply(CatalogReviewReplies[(index + k) % CatalogReviewReplies.Length],
                    createdAt.AddDays(8));
            }
        }
    }

    // === Derived values ===

    private static string CatalogEmail(string fullName) =>
        SlugGenerator.Slugify(fullName).Replace('-', '.') + CatalogEmailDomain;

    /// <summary>
    /// A checksum-valid 8-digit Czech IČO derived from the blueprint
    /// index (weights 8…2 over the first seven digits, mod 11). Real
    /// registration numbers are validated on the registration path, so
    /// seeded ones should survive the same rules.
    /// </summary>
    private static string CzechRegistrationNumber(int index)
    {
        var body = 6_400_000 + index * 911;
        var digits = $"{body:D7}";
        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            sum += (digits[i] - '0') * (8 - i);
        }

        var remainder = sum % 11;
        var checkDigit = remainder switch
        {
            0 => 1,
            1 => 0,
            _ => 11 - remainder,
        };

        return $"{digits}{checkDigit}";
    }

    /// <summary>
    /// The blueprint table is hand-written, so a copy-paste slip could
    /// hand two makers the same slug (a unique index) or the same owner
    /// name (which derives the e-mail). Fail here with the offending
    /// value rather than at SaveChanges with a constraint name.
    /// </summary>
    private static void GuardBlueprintsAreUnique()
    {
        Duplicates(CatalogMakerBlueprints.Select(b => b.Slug), "slug");
        Duplicates(CatalogMakerBlueprints.Select(b => b.Owner), "owner name");
        Duplicates(CatalogMakerBlueprints.Select(b => b.Company), "company name");
        Duplicates(CatalogCustomers, "customer name");

        static void Duplicates(IEnumerable<string> values, string label)
        {
            var duplicated = values
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicated.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Catalog blueprint {label} must be unique — duplicated: {string.Join(", ", duplicated)}.");
            }
        }
    }

    // === The dataset ===

    private sealed record CatalogMakerPlan(
        int Index,
        CatalogMakerBlueprint Blueprint,
        string MakerId,
        string UserId,
        string Email,
        string EmailNormalized,
        string Ico);

    private sealed record CatalogMakerBlueprint(
        string Company,
        string Slug,
        string Owner,
        string City,
        string Street,
        string HouseNumber,
        string Zip,
        string[] Categories,
        string Bio);

    private sealed record ProductTemplate(
        string Title,
        string Description,
        long PriceMinor,
        PriceType PriceType,
        FulfillmentType Fulfillment,
        int WeightGrams);

    private static readonly string[] BankCodes = ["0100", "0300", "0800", "2010", "5500", "6210"];

    /// <summary>Reviews per maker, by index — several makers have none yet.</summary>
    private static readonly int[] CatalogReviewCounts = [3, 1, 5, 0, 2, 4, 2, 1, 3, 0];

    private static readonly string[] CatalogCustomers =
    [
        "Kateřina Bláhová",
        "Martin Sýkora",
        "Barbora Krejčí",
        "Filip Urban",
        "Nikola Vávrová",
        "David Pokorný",
        "Simona Havlová",
        "Vojtěch Šťastný",
    ];

    /// <summary>
    /// Rating and body travel together — a 2★ paired with "předčilo
    /// očekávání" by an index mismatch would read as broken test data.
    /// The spread (2–5, average ≈ 4.2) gives the min-rating filter
    /// something to cut on.
    /// </summary>
    private static readonly (short Rating, string Body)[] CatalogReviews =
    [
        (5, "Vše proběhlo hladce, komunikace rychlá a výsledek přesně podle domluvy."),
        (5, "Kvalita předčila očekávání, určitě se vrátím s další zakázkou."),
        (5, "Zakázka byla hotová dřív, než jsem čekal, a zabalená opravdu pečlivě."),
        (4, "Poradili mi s materiálem a ušetřili mi tím peníze. Doporučuji."),
        (3, "Menší zdržení oproti slíbenému termínu, ale výsledek je moc pěkný."),
        (5, "Perfektní zpracování detailů, na fotkách to ani nevynikne."),
        (4, "Domluva bez problémů a cena odpovídá kvalitě."),
        (3, "Na jednom kuse byla drobná vada, po reklamaci ale hned přišla náhrada."),
        (5, "Přesně to, co jsme potřebovali do firmy. Objednáme znovu."),
        (4, "Rychlá odpověď na dotaz a férový přístup, spokojenost."),
        (2, "Výsledek je v pořádku, ale na odpověď jsem čekal skoro týden."),
        (5, "Skvělá práce, doporučuji všem, kdo chtějí něco netuctového."),
    ];

    private static readonly string[] CatalogReviewReplies =
    [
        "Děkujeme za hodnocení, těšíme se na další spolupráci!",
        "Moc děkujeme, rádi jsme pomohli.",
        "Díky za zpětnou vazbu — termíny budeme hlídat ještě pečlivěji.",
        "Děkujeme, vaší důvěry si vážíme.",
    ];

    private static readonly Dictionary<string, ProductTemplate[]> ProductTemplates = new(StringComparer.Ordinal)
    {
        [SeedCategories.Print3d] =
        [
            new("Prototyp na míru z PLA",
                "Vytiskneme váš model z PLA ve vrstvě 0,15 mm. Cena vychází z objemu materiálu a času tisku, uvedena je za menší díly.",
                48_000, PriceType.From, FulfillmentType.MadeToOrder, 300),
            new("Stojánek na telefon",
                "Stabilní stojánek z PETG s nastavitelným úhlem, unese i menší tablet.",
                27_000, PriceType.Fixed, FulfillmentType.InStock, 180),
            new("3D tisk z vašeho STL",
                "Nahrajte vlastní model a my se ozveme s nabídkou. Poradíme s materiálem, výplní i orientací tisku.",
                0, PriceType.OnRequest, FulfillmentType.MadeToOrder, 500),
            new("Náhradní díl na míru",
                "Rozbitý díl domodelujeme podle fotky nebo zlomku a vytiskneme z odolného ASA.",
                89_000, PriceType.From, FulfillmentType.MadeToOrder, 250),
        ],
        [SeedCategories.ClassicPrint] =
        [
            new("Vizitky 90×50 mm, 250 ks",
                "Oboustranný tisk na matný karton 350 g, volitelně s laminací nebo parciálním lakem.",
                62_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 300),
            new("Letáky A5, 500 ks",
                "Plnobarevný oboustranný tisk na křídu 135 g. Data přijímáme v PDF se spadávkou.",
                84_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 1_600),
            new("Katalog A4, vazba V1",
                "Šestnáctistránkový katalog sešitý dvěma skobami, obálka 250 g. Cena od nákladu 100 ks.",
                156_000, PriceType.From, FulfillmentType.MadeToOrder, 900),
            new("Samolepky na míru",
                "Vysekané samolepky do libovolného tvaru, vhodné i do exteriéru.",
                39_000, PriceType.From, FulfillmentType.MadeToOrder, 100),
        ],
        [SeedCategories.TextilePrint] =
        [
            new("Tričko s vlastním potiskem",
                "Bavlněné tričko 180 g s DTF potiskem vašeho motivu, velikosti S–3XL.",
                36_900, PriceType.Fixed, FulfillmentType.MadeToOrder, 200),
            new("Mikina s výšivkou loga",
                "Unisex mikina s kapucí, výšivka do 10×10 cm na hrudi nebo na zádech.",
                92_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 550),
            new("Firemní merch na míru",
                "Sestavíme kolekci pro celý tým — trička, mikiny i čepice. Ozveme se s nabídkou podle počtu kusů.",
                0, PriceType.OnRequest, FulfillmentType.MadeToOrder, 400),
            new("Plátěná taška s potiskem",
                "Bavlněná taška s dlouhými uchy a jednobarevným sítotiskem.",
                26_900, PriceType.Fixed, FulfillmentType.InStock, 120),
        ],
        [SeedCategories.LaserCnc] =
        [
            new("Gravírované prkénko se jménem",
                "Dubové prkénko 30×20 cm s gravírovaným věnováním podle vašeho přání.",
                54_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 900),
            new("Výřez loga z překližky",
                "Logo vyřezané z březové překližky 6 mm do rozměru 40×40 cm. Cena od jednoduchého tvaru.",
                78_000, PriceType.From, FulfillmentType.MadeToOrder, 600),
            new("Cedule z akrylátu",
                "Interiérová cedule z akrylátu 3 mm s gravírovaným textem a distančními sloupky.",
                63_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 400),
            new("Dřevěná jmenovka na dveře",
                "Malá jmenovka z masivu s gravírovaným jménem a oboustrannou páskou.",
                29_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 150),
        ],
        [SeedCategories.LargeFormat] =
        [
            new("Plakát A1 na fotopapíru",
                "Velkoformátový tisk A1 (594×841 mm) na lesklý fotopapír 200 g.",
                41_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 200),
            new("Banner 2×1 m s oky",
                "PVC banner 510 g s kovovými oky po obvodu, odolný povětrnosti.",
                104_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 1_500),
            new("Polep výlohy",
                "Řezaná grafika nebo tisk na fólii včetně montáže. Cena od jednoho metru čtverečního jednoduché grafiky.",
                89_000, PriceType.From, FulfillmentType.MadeToOrder, 300),
            new("Roll-up 85×200 cm",
                "Samonavíjecí roll-up včetně kazety a přepravní tašky.",
                168_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 3_500),
        ],
        [SeedCategories.Handmade] =
        [
            new("Keramický hrnek 350 ml",
                "Ručně točený hrnek z kameniny, glazura podle aktuální nabídky.",
                44_000, PriceType.Fixed, FulfillmentType.MadeToOrder, 350),
            new("Ručně šitá taška z plátna",
                "Pevná plátěná taška s podšívkou a vnitřní kapsou, šitá kus po kuse.",
                68_000, PriceType.Fixed, FulfillmentType.InStock, 300),
            new("Dárková sada na míru",
                "Sestavíme dárkovou sadu podle rozpočtu a příležitosti. Napište nám, co potřebujete.",
                0, PriceType.OnRequest, FulfillmentType.MadeToOrder, 800),
            new("Sada svíček ze sójového vosku",
                "Tři vonné svíčky ve skle, přírodní vosk a bavlněný knot.",
                52_000, PriceType.Fixed, FulfillmentType.InStock, 600),
        ],
    };

    /// <summary>
    /// Fifty makers spread over every launch category and across the
    /// country, with Praha and Brno appearing several times so the city
    /// filter has both partial matches ("Praha" → "Praha 4", "Praha 7")
    /// and single hits to work with.
    /// </summary>
    private static readonly CatalogMakerBlueprint[] CatalogMakerBlueprints =
    [
        new("Praha3D Studio s.r.o.", "praha3d-studio", "Jan Dvořák",
            "Praha 7", "Dělnická", "24", "170 00", [SeedCategories.Print3d],
            "FDM i SLA tisk pro produktové designéry. Prototyp bývá hotový do 48 hodin a s volbou materiálu poradíme."),
        new("Aditiva Brno s.r.o.", "aditiva-brno", "Petra Konečná",
            "Brno", "Veveří", "111", "602 00", [SeedCategories.Print3d],
            "Tiskneme z PETG, ASA i nylonu s uhlíkovými vlákny. Zaměřujeme se na funkční díly, které musí něco vydržet."),
        new("MakerHub Ostrava s.r.o.", "makerhub-ostrava", "Radek Kubíček",
            "Ostrava", "Nádražní", "140", "702 00", [SeedCategories.Print3d, SeedCategories.LaserCnc],
            "Dílna s deseti tiskárnami a velkým laserem. Zvládneme jeden kus i malou sérii včetně kompletace a balení."),
        new("Plzeňská tisková dílna s.r.o.", "plzenska-tiskova-dilna", "Marta Šimková",
            "Plzeň", "Klatovská třída", "87", "301 00", [SeedCategories.ClassicPrint],
            "Rodinná tiskárna se čtyřiceti lety praxe. Vizitky, letáky i katalogy, kontrolu tiskových dat děláme zdarma."),
        new("Formax Olomouc s.r.o.", "formax-olomouc", "Tomáš Navrátil",
            "Olomouc", "Wolkerova", "33", "779 00", [SeedCategories.Print3d],
            "3D tisk technických dílů a náhradních součástek. Hlídáme rozměrovou přesnost i čitelnost závitů."),
        new("Textilka Liberec s.r.o.", "textilka-liberec", "Hana Malá",
            "Liberec", "Pražská", "18", "460 01", [SeedCategories.TextilePrint],
            "Sítotisk a DTF na trička, mikiny a tašky. Potiskneme jeden kus stejně rádi jako tisícovku firemního merche."),
        new("LaserWorks Hradec s.r.o.", "laserworks-hradec", "Michal Beneš",
            "Hradec Králové", "Gočárova třída", "62", "500 02", [SeedCategories.LaserCnc],
            "Řežeme a gravírujeme dřevo, překližku, akrylát i kůži. Podklady si připravíme z fotky nebo ruční skici."),
        new("Jihočeská tiskárna s.r.o.", "jihoceska-tiskarna", "Věra Bártová",
            "České Budějovice", "Lannova třída", "9", "370 01", [SeedCategories.ClassicPrint, SeedCategories.LargeFormat],
            "Ofset i digitál pod jednou střechou. Tiskneme obecní zpravodaje, plakáty a kompletní firemní tiskoviny."),
        new("Pardubický 3D ateliér s.r.o.", "pardubicky-3d-atelier", "Lukáš Horák",
            "Pardubice", "Sladkovského", "45", "530 02", [SeedCategories.Print3d, SeedCategories.Handmade],
            "Tiskneme figurky, herní doplňky a dárkové předměty. Modely dobarvujeme ručně, takže každý kus je originál."),
        new("Zlín Print s.r.o.", "zlin-print", "Alena Kučerová",
            "Zlín", "Osvoboditelů", "91", "760 01", [SeedCategories.ClassicPrint],
            "Digitální tisk s dodáním do 24 hodin. Specializujeme se na svatební a firemní tiskoviny s ražbou."),
        new("Vysočina Laser s.r.o.", "vysocina-laser", "Josef Doležal",
            "Jihlava", "Havlíčkova", "27", "586 01", [SeedCategories.LaserCnc],
            "Laserové řezání překližky, MDF a plexi. Vyrábíme cedule, svatební dekorace i díly pro modeláře."),
        new("Karlovarská reklama s.r.o.", "karlovarska-reklama", "Ivana Pospíšilová",
            "Karlovy Vary", "T. G. Masaryka", "14", "360 01", [SeedCategories.LargeFormat],
            "Velkoformátový tisk bannerů, plachet a polepů. Montáž po celém kraji zajistíme vlastními lidmi."),
        new("Ústecký merch s.r.o.", "ustecky-merch", "Petr Sedláček",
            "Ústí nad Labem", "Masarykova", "205", "400 01", [SeedCategories.TextilePrint],
            "Firemní oblečení od návrhu po expedici. Vyšíváme loga a tiskneme kolekce pro kapely i sportovní kluby."),
        new("Opavská dílna s.r.o.", "opavska-dilna", "Ludmila Richterová",
            "Opava", "Ostrožná", "8", "746 01", [SeedCategories.Handmade],
            "Ručně točená keramika a glazury míchané přímo v dílně. Hrnky, misky i sady na kávu podle vlastního návrhu."),
        new("Kladno Prototyping s.r.o.", "kladno-prototyping", "Ondřej Kratochvíl",
            "Kladno", "Huťská", "3", "272 01", [SeedCategories.Print3d],
            "Prototypy a přípravky pro strojírny. Tiskneme z technických materiálů a rozměry proměřujeme kus po kuse."),
        new("Táborská knihtiskárna s.r.o.", "taborska-knihtiskarna", "Miroslav Bureš",
            "Tábor", "Křižíkovo náměstí", "12", "390 01", [SeedCategories.ClassicPrint],
            "Tiskneme knihy, sborníky a diplomové práce. Vážeme V1, V2 i šitou vazbu s tvrdými deskami."),
        new("Znojemská grafika s.r.o.", "znojemska-grafika", "Dagmar Vlčková",
            "Znojmo", "Horní náměstí", "6", "669 02", [SeedCategories.ClassicPrint, SeedCategories.LargeFormat],
            "Grafické studio s vlastním tiskem. Navrhneme logo, vizuál i celý katalog a rovnou ho vytiskneme."),
        new("Přerov 3D s.r.o.", "prerov-3d", "Marek Sedlák",
            "Přerov", "Palackého", "19", "750 02", [SeedCategories.Print3d],
            "Tiskneme náhradní díly na spotřebiče a zemědělskou techniku. Chybějící model zvládneme domodelovat."),
        new("Trutnovská výšivka s.r.o.", "trutnovska-vysivka", "Zuzana Hrušková",
            "Trutnov", "Krakonošovo náměstí", "21", "541 01", [SeedCategories.TextilePrint],
            "Strojová výšivka na oděvy i doplňky. Digitalizaci loga do vyšívacího formátu máte u nás v ceně."),
        new("Polepy Praha 9 s.r.o.", "polepy-praha-9", "Vít Charvát",
            "Praha 9", "Kolbenova", "40", "190 00", [SeedCategories.LargeFormat],
            "Polepy aut, výloh a firemních prostor. Řezaná grafika i tisk na fólii s laminací proti UV."),
        new("Mostecká tiskárna s.r.o.", "mostecka-tiskarna", "Renata Šťastná",
            "Most", "Moskevská", "3", "434 01", [SeedCategories.ClassicPrint],
            "Malonákladový digitální tisk pro školy a spolky. Brožury, diplomy a vstupenky s číslováním."),
        new("Děčínské dřevo s.r.o.", "decinske-drevo", "Karel Vondráček",
            "Děčín", "Teplická", "55", "405 02", [SeedCategories.LaserCnc, SeedCategories.Handmade],
            "Truhlářská dílna s laserem. Gravírujeme prkénka, vyrábíme dřevěné hračky a dekorace z masivu."),
        new("Frýdecká 3D dílna s.r.o.", "frydecka-3d-dilna", "Simona Bartošová",
            "Frýdek-Místek", "Hlavní třída", "8", "738 01", [SeedCategories.Print3d],
            "Barevný 3D tisk hraček, dekorací a dárků. Skladem máme přes dvacet odstínů filamentu."),
        new("Havířovský textil s.r.o.", "havirovsky-textil", "Jaroslav Pech",
            "Havířov", "Dlouhá třída", "17", "736 01", [SeedCategories.TextilePrint],
            "Potisk pracovních oděvů a reflexních vest. Dodáváme kompletní vybavení pro firmy i sportovní kluby."),
        new("Prostějovská galanterie s.r.o.", "prostejovska-galanterie", "Kamila Nedbalová",
            "Prostějov", "Žižkovo náměstí", "2", "796 01", [SeedCategories.Handmade, SeedCategories.TextilePrint],
            "Šijeme tašky, penály a zástěry z pevného plátna. Každý kus doplníme jménem nebo logem."),
        new("Třebíčská tiskárna s.r.o.", "trebicska-tiskarna", "Vladimír Pokorný",
            "Třebíč", "Karlovo náměstí", "30", "674 01", [SeedCategories.ClassicPrint],
            "Tiskneme etikety, samolepky a obaly pro malé výrobce potravin. Poradíme i s povinnými údaji na obalu."),
        new("Chebský plast s.r.o.", "chebsky-plast", "Martina Fialová",
            "Cheb", "Svobody", "12", "350 02", [SeedCategories.Print3d],
            "Technický 3D tisk a drobná výroba plastových dílů. Zakázku posoudíme do jednoho pracovního dne."),
        new("Chomutovská reklama s.r.o.", "chomutovska-reklama", "Roman Blažek",
            "Chomutov", "Revoluční", "40", "430 01", [SeedCategories.LargeFormat, SeedCategories.ClassicPrint],
            "Reklamní agentura s vlastní výrobou. Od vizitky po plachtu na fasádu vyřídíme vše na jednom místě."),
        new("Písecká keramika s.r.o.", "pisecka-keramika", "Eliška Vaňková",
            "Písek", "Velké náměstí", "5", "397 01", [SeedCategories.Handmade],
            "Malá keramická dílna u Otavy. Točíme hrnky a mísy z kameniny, pálíme na 1250 °C."),
        new("Krnovské modely s.r.o.", "krnovske-modely", "Štěpán Uhlíř",
            "Krnov", "Hlavní náměstí", "11", "794 01", [SeedCategories.Print3d, SeedCategories.Handmade],
            "Tiskneme a stavíme modely budov, dioráma i architektonické makety podle výkresů."),
        new("Gravírka Praha 4 s.r.o.", "gravirka-praha-4", "Jitka Křížová",
            "Praha 4", "Budějovická", "63", "140 00", [SeedCategories.LaserCnc],
            "Gravírujeme dárky, medaile a firemní pozornosti. Jednorázovou zakázku zvládneme i na počkání."),
        new("Berounský papír s.r.o.", "berounsky-papir", "Aleš Mach",
            "Beroun", "Husovo náměstí", "22", "266 01", [SeedCategories.ClassicPrint],
            "Tisk na recyklované a přírodní papíry. Pro ekologicky zaměřené značky děláme obaly i etikety."),
        new("Boleslavská tiskárna s.r.o.", "boleslavska-tiskarna", "Nikola Šimonová",
            "Mladá Boleslav", "Železná", "4", "293 01", [SeedCategories.ClassicPrint, SeedCategories.LargeFormat],
            "Průmyslové tiskoviny, manuály a bezpečnostní značení. Dodáváme firmám z automobilového průmyslu."),
        new("Náchodský merch s.r.o.", "nachodsky-merch", "Adam Toman",
            "Náchod", "Kamenice", "15", "547 01", [SeedCategories.TextilePrint],
            "Trička, mikiny a čepice pro kapely, festivaly i e-shopy. Zboží u nás můžete i skladovat a expedovat."),
        new("Nymburské sklo a laser s.r.o.", "nymburske-sklo-a-laser", "Tereza Jandová",
            "Nymburk", "Palackého třída", "60", "288 02", [SeedCategories.LaserCnc],
            "Gravírujeme do skla i nerezu. Vyrábíme ceny pro soutěže, jmenovky a technické štítky."),
        new("Slánská dílna s.r.o.", "slanska-dilna", "Pavel Rous",
            "Slaný", "Wilsonova", "9", "274 01", [SeedCategories.Handmade],
            "Ruční výroba svíček a mýdel z přírodních surovin. Dárkové sady zabalíme podle přání."),
        new("Kutnohorská knihárna s.r.o.", "kutnohorska-kniharna", "Marcela Švecová",
            "Kutná Hora", "Husova", "3", "284 01", [SeedCategories.ClassicPrint, SeedCategories.Handmade],
            "Ruční knihařství a restaurování vazeb. Šijeme deníky, alba i diplomové práce na míru."),
        new("Uherský 3D tisk s.r.o.", "uhersky-3d-tisk", "Dominik Jurča",
            "Uherské Hradiště", "Mariánské náměstí", "18", "686 01", [SeedCategories.Print3d],
            "Tisk z pružného TPU i tvrdého ABS. Vyrábíme držáky, pouzdra a montážní přípravky."),
        new("Valašská tiskárna s.r.o.", "valasska-tiskarna", "Blanka Struhařová",
            "Valašské Meziříčí", "Zašovská", "25", "757 01", [SeedCategories.ClassicPrint],
            "Tiskneme pro obce a spolky celého Valašska. Zpravodaje, kalendáře a pozvánky vozíme zdarma."),
        new("Šumperská grafika s.r.o.", "sumperska-grafika", "Erik Novotný",
            "Šumperk", "Hlavní třída", "12", "787 01", [SeedCategories.LargeFormat],
            "Velkoformátové plakáty, roll-upy a výstavní systémy. Grafiku připravíme z vašich podkladů."),
        new("Bruntálská dřevovýroba s.r.o.", "bruntalska-drevovyroba", "Libor Ševčík",
            "Bruntál", "Partyzánská", "6", "792 01", [SeedCategories.LaserCnc, SeedCategories.Handmade],
            "Vyrábíme dřevěné dekorace, jmenovky a svatební doplňky. Materiál vozíme z místních pil."),
        new("Litoměřická tiskárna s.r.o.", "litomericka-tiskarna", "Anna Kolářová",
            "Litoměřice", "Mírové náměstí", "21", "412 01", [SeedCategories.ClassicPrint],
            "Rodinný podnik s ofsetovým strojem i digitálem. Malé náklady tiskneme klidně od padesáti kusů."),
        new("Rakovnický potisk s.r.o.", "rakovnicky-potisk", "Jakub Bednář",
            "Rakovník", "Husovo náměstí", "30", "269 01", [SeedCategories.TextilePrint, SeedCategories.LargeFormat],
            "Potisk textilu a reklamních předmětů. Vzorek uděláme zdarma před spuštěním celé série."),
        new("Klatovská laserovna s.r.o.", "klatovska-laserovna", "Veronika Šmídová",
            "Klatovy", "Plánická", "5", "339 01", [SeedCategories.LaserCnc],
            "Přesné řezání akrylátu, dřeva a filcu. Specializujeme se na interiérové cedule a dekorace."),
        new("Prototypy Praha 5 s.r.o.", "prototypy-praha-5", "Milan Vávra",
            "Praha 5", "Radlická", "112", "150 00", [SeedCategories.Print3d, SeedCategories.LaserCnc],
            "Kombinujeme 3D tisk s laserem. Vyrobíme prototyp, obal i prezentační stojan pro váš produkt."),
        new("Benešovská tiskárna s.r.o.", "benesovska-tiskarna", "Klára Hejná",
            "Benešov", "Tyršova", "8", "256 01", [SeedCategories.ClassicPrint],
            "Tiskneme pro školy, úřady i malé firmy. Formuláře, brožury a razítka zvládneme na počkání."),
        new("Jindřichohradecký textil s.r.o.", "jindrichohradecky-textil", "Radim Kadlec",
            "Jindřichův Hradec", "Nádražní", "27", "377 01", [SeedCategories.TextilePrint],
            "Sítotisk s vlastní míchárnou barev. Zvládneme i netradiční materiály jako len nebo plátno."),
        new("Brněnská pryskyřice s.r.o.", "brnenska-pryskyrice", "Denisa Šebková",
            "Brno", "Gajdošova", "24", "615 00", [SeedCategories.Print3d],
            "Tiskneme šperky, dekorace a drobné dárky z pryskyřice. Detaily vypadají jako přesný odlitek."),
        new("Vyškovská reklama s.r.o.", "vyskovska-reklama", "Tomáš Ryba",
            "Vyškov", "Masarykovo náměstí", "13", "682 01", [SeedCategories.LargeFormat, SeedCategories.TextilePrint],
            "Kompletní reklamní servis pro region. Bannery, polepy i potištěné oblečení pro váš tým."),
        new("Novojičínská manufaktura s.r.o.", "novojicinska-manufaktura", "Gabriela Pilařová",
            "Nový Jičín", "Masarykovo náměstí", "4", "741 01", [SeedCategories.Handmade, SeedCategories.LaserCnc],
            "Ruční výroba dárků z papíru, dřeva a textilu. Každou zakázku balíme jako dárek, protože jím většinou je."),
    ];
}
