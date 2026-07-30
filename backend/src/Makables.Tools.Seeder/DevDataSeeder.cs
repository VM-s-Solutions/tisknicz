using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
using Makables.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Makables.Tools.Seeder;

/// <summary>
/// Development / test data seeder. Builds a realistic CZ marketplace
/// snapshot ON TOP of the reference data the migrations already seed
/// (country CZ, CountryConfiguration, the six launch categories, email
/// templates): users of all three roles, verified + unverified makers,
/// products across every category, orders in every <see cref="OrderState"/>,
/// order-message threads, reviews, and the denormalized maker catalog stats.
///
/// <para>
/// Safety: refuses to run against a non-local host unless
/// <c>--allow-remote</c> is passed, and always refuses when the host or
/// database name contains "prod". Idempotent via the sentinel admin row —
/// a second run is a no-op; <c>--reset</c> deletes all <c>seed-*</c> rows
/// first and reseeds. Everything runs in a single transaction.
/// </para>
///
/// <para>
/// All ids are deterministic (<c>seed-*</c>) so tests can reference them,
/// all audit stamps use actor <c>seed</c> (matching the migration seeds),
/// and order numbers use the reserved 9xxx range of the ADR 0009 format
/// (<c>M-CZ-{YYYY}9NNN</c>) so the live <c>IOrderNumberGenerator</c>
/// sequence (which starts at 0001) cannot collide with them in dev.
/// </para>
/// </summary>
public sealed class DevDataSeeder(
    MakablesDbContext db,
    IPasswordHasher passwordHasher,
    SeedClock clock,
    ILogger<DevDataSeeder> logger)
{
    /// <summary>Shared password for every seeded account.</summary>
    public const string SharedPassword = "SeedHeslo.123";

    private const string Actor = "seed";
    private const string Country = "CZ";
    private const string Currency = "CZK";
    private const int VatRateBp = 2100;
    private const int PlatformFeeRateBp = 1500;
    private const long ZasilkovnaShippingMinor = 8_900;
    private const int AutoDeliverWindowDays = 7;
    private const string SeedIdPrefix = "seed-";
    private const string AdminUserId = "seed-user-admin";

    private readonly List<Order> _orders = [];
    private readonly List<Review> _reviews = [];
    private readonly List<Maker> _makers = [];
    private int _messageCount;
    private DateTimeOffset _now;

    public async Task<int> RunAsync(bool reset, bool allowRemote, bool migrate, CancellationToken ct)
    {
        if (!TargetIsSafe(allowRemote))
        {
            return 2;
        }

        if (!await SchemaIsReadyAsync(migrate, ct))
        {
            return 3;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (reset)
        {
            await DeleteSeedDataAsync(ct);
            await db.SaveChangesAsync(ct);
        }

        var alreadySeeded = await db.Set<User>()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == AdminUserId, ct);
        if (alreadySeeded)
        {
            logger.LogInformation(
                "Seed data already present (sentinel {Sentinel} exists). Nothing to do — rerun with --reset to reseed.",
                AdminUserId);
            return 0;
        }

        _now = DateTimeOffset.UtcNow;
        clock.UtcNow = _now;

        SeedAll();

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Seed complete: {Users} users, {Makers} makers, {Products} products, {Orders} orders, {Reviews} reviews.",
            db.ChangeTracker.Entries<User>().Count(),
            _makers.Count,
            db.ChangeTracker.Entries<Product>().Count(),
            _orders.Count,
            _reviews.Count);
        logger.LogInformation(
            "Every account (admin@makables.test, jana.novakova@makables.test, karel.tiskar@makables.test, …) uses the password '{Password}'.",
            SharedPassword);
        return 0;
    }

    // === Safety rails ===

    private bool TargetIsSafe(bool allowRemote)
    {
        var connectionString = db.Database.GetConnectionString();
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var host = csb.Host ?? string.Empty;
        var database = csb.Database ?? string.Empty;

        if (host.Contains("prod", StringComparison.OrdinalIgnoreCase)
            || database.Contains("prod", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Refusing to seed: target {Host}/{Database} looks like a production database. There is no override for this check.",
                host, database);
            return false;
        }

        var isLocal = host is "localhost" or "127.0.0.1" or "::1";
        if (!isLocal && !allowRemote)
        {
            logger.LogError(
                "Refusing to seed non-local host {Host}. Pass --allow-remote if this really is a disposable environment.",
                host);
            return false;
        }

        return true;
    }

    private async Task<bool> SchemaIsReadyAsync(bool migrate, CancellationToken ct)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            return true;
        }

        if (!migrate)
        {
            logger.LogError(
                "Database schema is behind by {Count} migration(s) (first: {First}). Run with --migrate or apply them via 'dotnet ef database update' first.",
                pending.Count, pending[0]);
            return false;
        }

        logger.LogInformation("Applying {Count} pending migration(s)…", pending.Count);
        await db.Database.MigrateAsync(ct);
        return true;
    }

    // === Reset ===

    private async Task DeleteSeedDataAsync(CancellationToken ct)
    {
        logger.LogInformation("Deleting existing seed-* rows…");

        // Children before parents, everything through the tracker inside
        // the surrounding transaction. Small fixed dataset — tracked hard
        // deletes are simpler and safer here than per-table raw SQL.
        db.RemoveRange(await db.Set<Review>().IgnoreQueryFilters()
            .Where(r => r.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<Dispute>().IgnoreQueryFilters()
            .Where(d => d.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<OrderMessage>().IgnoreQueryFilters()
            .Where(m => m.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<Order>().IgnoreQueryFilters()
            .Where(o => o.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<Product>().IgnoreQueryFilters()
            .Where(p => p.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<MakerCategory>()
            .Where(mc => mc.MakerId.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<Maker>().IgnoreQueryFilters()
            .Where(m => m.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<Address>().IgnoreQueryFilters()
            .Where(a => a.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
        db.RemoveRange(await db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Id.StartsWith(SeedIdPrefix)).ToListAsync(ct));
    }

    // === The dataset ===

    private void SeedAll()
    {
        var passwordHash = passwordHasher.Hash(SharedPassword);

        // --- Admin + customers ---
        AddUser(AdminUserId, "admin@makables.test", UserRole.Admin,
            "Správce Platformy", passwordHash, confirmed: true, DaysAgo(90));

        var jana = AddUser("seed-user-cust-01", "jana.novakova@makables.test", UserRole.Customer,
            "Jana Nováková", passwordHash, confirmed: true, DaysAgo(45));
        var petr = AddUser("seed-user-cust-02", "petr.svoboda@makables.test", UserRole.Customer,
            "Petr Svoboda", passwordHash, confirmed: true, DaysAgo(40));
        var eva = AddUser("seed-user-cust-03", "eva.dvorakova@makables.test", UserRole.Customer,
            "Eva Dvořáková", passwordHash, confirmed: true, DaysAgo(35));
        // Unconfirmed customer — tests the email-confirmation gating.
        AddUser("seed-user-cust-04", "tomas.marek@makables.test", UserRole.Customer,
            "Tomáš Marek", passwordHash, confirmed: false, DaysAgo(2));

        // --- Makers (user + legal-seat address + maker row + categories) ---
        var karel = AddUser("seed-user-maker-01", "karel.tiskar@makables.test", UserRole.Maker,
            "Karel Tiskař", passwordHash, confirmed: true, DaysAgo(60));
        var maker1 = AddMaker("seed-maker-01", karel, ico: "12345679", vatId: "CZ12345679",
            company: "PrintLab s.r.o.", slug: "printlab",
            street: "Korunní", houseNumber: "1208/12", city: "Praha", zip: "120 00",
            bio: "Malá pražská dílna zaměřená na precizní FDM a SLA tisk. Tiskneme od prototypů po malé série, poradíme s materiálem i modelem.",
            bank: "123456789/0800", pickup: true,
            pickupNote: "Osobní odběr po domluvě, všední dny 9–17 h, zvonek PrintLab.",
            verified: true, createdAt: DaysAgo(60),
            categories: [SeedCategories.Print3d]);

        var marie = AddUser("seed-user-maker-02", "marie.vltavska@makables.test", UserRole.Maker,
            "Marie Vltavská", passwordHash, confirmed: true, DaysAgo(55));
        var maker2 = AddMaker("seed-maker-02", marie, ico: "87654326", vatId: "CZ87654326",
            company: "Tiskárna Vltava s.r.o.", slug: "tiskarna-vltava",
            street: "Nádražní", houseNumber: "32", city: "Praha", zip: "150 00",
            bio: "Klasický ofset i digitál pod jednou střechou. Vizitky, svatební oznámení, plakáty a velkoformátové bannery do druhého dne.",
            bank: "987654321/0100", pickup: false, pickupNote: null,
            verified: true, createdAt: DaysAgo(55),
            categories: [SeedCategories.ClassicPrint, SeedCategories.LargeFormat]);

        var ondrej = AddUser("seed-user-maker-03", "ondrej.barvir@makables.test", UserRole.Maker,
            "Ondřej Barvíř", passwordHash, confirmed: true, DaysAgo(50));
        var maker3 = AddMaker("seed-maker-03", ondrej, ico: "25596641", vatId: "CZ25596641",
            company: "Textilka Brno s.r.o.", slug: "textilka-brno",
            street: "Cejl", houseNumber: "76", city: "Brno", zip: "602 00",
            bio: "Sítotisk, DTF i výšivka na textil. Potiskneme jeden kus stejně rádi jako celý firemní merch.",
            bank: "555666777/0300", pickup: false, pickupNote: null,
            verified: true, createdAt: DaysAgo(50),
            categories: [SeedCategories.TextilePrint]);

        var lucie = AddUser("seed-user-maker-04", "lucie.rezava@makables.test", UserRole.Maker,
            "Lucie Řezavá", passwordHash, confirmed: true, DaysAgo(45));
        var maker4 = AddMaker("seed-maker-04", lucie, ico: "45012342", vatId: null,
            company: "LaserCut Ostrava s.r.o.", slug: "lasercut-ostrava",
            street: "Stodolní", houseNumber: "9", city: "Ostrava", zip: "702 00",
            bio: "Laserové řezání a gravírování dřeva, překližky a akrylátu. Vlastní návrhy i zakázková výroba podle vašich podkladů.",
            bank: "111222333/2010", pickup: true,
            pickupNote: "Vyzvednutí v dílně na Stodolní, po–pá 10–16 h.",
            verified: true, createdAt: DaysAgo(45),
            categories: [SeedCategories.LaserCnc, SeedCategories.Handmade]);

        // Unverified maker — invisible in the public catalog, visible in
        // the admin verification queue.
        var alena = AddUser("seed-user-maker-05", "alena.lipova@makables.test", UserRole.Maker,
            "Alena Lipová", passwordHash, confirmed: true, DaysAgo(3));
        var maker5 = AddMaker("seed-maker-05", alena, ico: "73000001", vatId: null,
            company: "Dílna U Lípy s.r.o.", slug: "dilna-u-lipy",
            street: "Dolní náměstí", houseNumber: "17", city: "Olomouc", zip: "779 00",
            bio: "Ručně točená keramika z malé olomoucké dílny.",
            bank: null, pickup: false, pickupNote: null,
            verified: false, createdAt: DaysAgo(3),
            categories: [SeedCategories.Handmade]);

        // --- Products ---
        var vaza = AddProduct("seed-product-3d-01", maker1, SeedCategories.Print3d,
            "Váza z PLA – vlastní barva", "Dekorativní spirálová váza tištěná z PLA. Barvu vybíráte z 12 odstínů, výška 18 cm.",
            45_000, PriceType.Fixed, FulfillmentType.InStock, weightGrams: 300, DaysAgo(30));
        AddProduct("seed-product-3d-02", maker1, SeedCategories.Print3d,
            "3D tisk na zakázku (vlastní STL)", "Nahrajte vlastní model a my ho vytiskneme. Cena dle objemu materiálu a času tisku — ozveme se s nabídkou.",
            0, PriceType.OnRequest, FulfillmentType.MadeToOrder, weightGrams: 500, DaysAgo(30));
        var drzak = AddProduct("seed-product-3d-03", maker1, SeedCategories.Print3d,
            "Držák na sluchátka pod stůl", "Praktický držák na sluchátka s montáží pod desku stolu, PETG, černá.",
            29_000, PriceType.Fixed, FulfillmentType.InStock, weightGrams: 250, DaysAgo(28));
        var drak = AddProduct("seed-product-3d-04", maker1, SeedCategories.Print3d,
            "Kloubový drak – artikulovaný", "Ohebný drak tištěný vcelku, bez montáže. Délka 30–60 cm dle volby, cena od menší varianty.",
            69_000, PriceType.From, FulfillmentType.MadeToOrder, weightGrams: 400, DaysAgo(26));

        var vizitky = AddProduct("seed-product-print-01", maker2, SeedCategories.ClassicPrint,
            "Vizitky 90×50 mm, 250 ks", "Oboustranný tisk na 350g matný papír, možnost laminace. Data v PDF.",
            59_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 300, DaysAgo(25));
        AddProduct("seed-product-print-02", maker2, SeedCategories.ClassicPrint,
            "Svatební oznámení – sada 50 ks", "Tisk na strukturovaný papír, včetně obálek. Cena od jednoduché jednostranné varianty.",
            119_000, PriceType.From, FulfillmentType.MadeToOrder, weightGrams: 400, DaysAgo(24));
        var plakat = AddProduct("seed-product-print-03", maker2, SeedCategories.LargeFormat,
            "Plakát A1 na fotopapíru", "Velkoformátový tisk A1 (594×841 mm) na lesklý fotopapír 200 g.",
            39_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 200, DaysAgo(23));
        var banner = AddProduct("seed-product-print-04", maker2, SeedCategories.LargeFormat,
            "Banner 2×1 m s oky", "PVC banner 510 g s kovovými oky po obvodu, odolný povětrnosti.",
            99_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 1_500, DaysAgo(22));

        var tricko = AddProduct("seed-product-tex-01", maker3, SeedCategories.TextilePrint,
            "Tričko s vlastním potiskem", "Bavlněné tričko 180 g s DTF potiskem vašeho motivu. Velikosti S–3XL.",
            34_900, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 200, DaysAgo(21));
        var mikina = AddProduct("seed-product-tex-02", maker3, SeedCategories.TextilePrint,
            "Mikina s výšivkou loga", "Unisex mikina s kapucí, výšivka loga do 10×10 cm na hrudi.",
            89_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 550, DaysAgo(20));
        var taska = AddProduct("seed-product-tex-03", maker3, SeedCategories.TextilePrint,
            "Plátěná taška s potiskem", "Bavlněná taška s dlouhými uchy a jednobarevným sítotiskem.",
            24_900, PriceType.Fixed, FulfillmentType.InStock, weightGrams: 120, DaysAgo(19));

        var prkenko = AddProduct("seed-product-las-01", maker4, SeedCategories.LaserCnc,
            "Gravírované prkénko se jménem", "Dubové prkénko 30×20 cm s gravírovaným věnováním dle přání.",
            52_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 900, DaysAgo(18));
        var vyrez = AddProduct("seed-product-las-02", maker4, SeedCategories.LaserCnc,
            "Výřez loga z překližky", "Logo vyřezané z březové překližky 6 mm, do rozměru 40×40 cm. Cena od jednoduchého tvaru.",
            75_000, PriceType.From, FulfillmentType.MadeToOrder, weightGrams: 600, DaysAgo(17));
        // Soft-deleted product — tests the IsActive query filter.
        var ozdoby = AddProduct("seed-product-las-03", maker4, SeedCategories.Handmade,
            "Dřevěné vánoční ozdoby – sada 12 ks", "Sezónní sada laserem řezaných ozdob z topolové překližky.",
            38_000, PriceType.Fixed, FulfillmentType.InStock, weightGrams: 150, DaysAgo(16));
        ozdoby.MarkDeactivated(Actor, DaysAgo(9));

        // Product of the unverified maker — must stay out of the catalog.
        AddProduct("seed-product-hand-01", maker5, SeedCategories.Handmade,
            "Keramický hrnek 350 ml", "Ručně točený hrnek z kameniny, glazura dle nabídky.",
            42_000, PriceType.Fixed, FulfillmentType.MadeToOrder, weightGrams: 350, DaysAgo(2));

        // --- Orders across every state ---

        // 9001: fresh PendingPayment.
        AddOrder(9001, jana, maker1, vaza, HoursAgo(2));

        // 9002: PendingPayment with a reserved Comgate session (retry window).
        var o2 = AddOrder(9002, petr, maker2, vizitky, HoursAgo(6));
        clock.UtcNow = HoursAgo(6).AddMinutes(5);
        Ensure(o2.ReservePaymentSession("SEED-PAY-9002",
            "https://payments.comgate.cz/client/instructions/index?id=SEED-PAY-9002", clock), "reserve 9002");

        // 9003: Paid, waiting for the maker to accept.
        var o3 = AddOrder(9003, eva, maker1, drzak, DaysAgo(1));
        Pay(o3, DaysAgo(1).AddMinutes(10));

        // 9004: Accepted, with a live message thread (1 unread for the maker).
        var o4 = AddOrder(9004, jana, maker3, tricko, DaysAgo(3),
            notes: "Prosím velikost M, motiv posílám v příloze.");
        Pay(o4, DaysAgo(3).AddMinutes(15));
        Accept(o4, DaysAgo(2));
        AddMessage(o4, OrderMessageAuthorRole.Customer, jana,
            "Dobrý den, šlo by místo velikosti M poslat L? Ještě jste snad netiskli.", DaysAgo(2).AddHours(1), unread: false);
        AddMessage(o4, OrderMessageAuthorRole.Maker, ondrej,
            "Dobrý den, jasně, velikost v objednávce upravíme na L. Tiskneme zítra.", DaysAgo(2).AddHours(3), unread: false);
        AddMessage(o4, OrderMessageAuthorRole.Customer, jana,
            "Děkuji! Ještě prosím účtenku na firmu, IČO pošlu zprávou.", DaysAgo(1), unread: true);

        // 9005: Shipped via Zásilkovna, tracking + 1 unread message for the customer.
        var o5 = AddOrder(9005, petr, maker1, drak, DaysAgo(5));
        Pay(o5, DaysAgo(5).AddHours(1));
        Accept(o5, DaysAgo(4));
        Ship(o5, DaysAgo(2), "Z1029384756");
        AddMessage(o5, OrderMessageAuthorRole.Maker, karel,
            "Drak je vytištěný a předaný Zásilkovně, číslo zásilky najdete v objednávce.", DaysAgo(2), unread: true);

        // 9006: Delivered (customer confirmed).
        var o6 = AddOrder(9006, eva, maker2, plakat, DaysAgo(8));
        Pay(o6, DaysAgo(8).AddMinutes(30));
        Accept(o6, DaysAgo(7));
        Ship(o6, DaysAgo(5), "Z5647382910");
        Deliver(o6, DaysAgo(1));

        // 9007–9009: Completed with reviews.
        var o7 = AddOrder(9007, jana, maker1, vaza, DaysAgo(20));
        RunToCompleted(o7, paidAt: DaysAgo(20).AddHours(1), acceptedAt: DaysAgo(19),
            shippedAt: DaysAgo(17), carrierRef: "Z1112223334", deliveredAt: DaysAgo(14), completedAt: DaysAgo(12));
        AddReview("seed-review-01", o7, jana, rating: 5,
            "Krásný tisk, žádné viditelné vrstvy a barva přesně podle objednávky. Doporučuji!", DaysAgo(11));

        var o8 = AddOrder(9008, petr, maker3, mikina, DaysAgo(18));
        RunToCompleted(o8, paidAt: DaysAgo(18).AddHours(2), acceptedAt: DaysAgo(17),
            shippedAt: DaysAgo(15), carrierRef: "Z4445556667", deliveredAt: DaysAgo(13), completedAt: DaysAgo(10));
        var r8 = AddReview("seed-review-02", o8, petr, rating: 4,
            "Výšivka super, jen dodání trvalo o pár dní déle, než jsem čekal.", DaysAgo(9));
        r8.AddReply("Děkujeme za zpětnou vazbu! Omlouváme se za zdržení, čekali jsme na dodávku mikin od výrobce.", DaysAgo(8));

        var o9 = AddOrder(9009, eva, maker4, prkenko, DaysAgo(15));
        RunToCompleted(o9, paidAt: DaysAgo(15).AddHours(1), acceptedAt: DaysAgo(14),
            shippedAt: DaysAgo(12), carrierRef: "Z7778889990", deliveredAt: DaysAgo(10), completedAt: DaysAgo(8));
        AddReview("seed-review-03", o9, eva, rating: 5,
            "Gravírování je dokonalé, dřevo krásně voní. Dárek měl obrovský úspěch.", DaysAgo(7));

        // 9010: Cancelled by the customer before payment.
        var o10 = AddOrder(9010, jana, maker2, banner, DaysAgo(10));
        clock.UtcNow = DaysAgo(9);
        Ensure(o10.Cancel(clock), "cancel 9010");

        // 9011: fully Refunded from Paid.
        var o11 = AddOrder(9011, petr, maker4, vyrez, DaysAgo(12));
        Pay(o11, DaysAgo(12).AddHours(1));
        clock.UtcNow = DaysAgo(11);
        Ensure(o11.Refund(clock, o11.TotalAmountMinor, acknowledgePostPayout: false), "refund 9011");

        // 9012: Disputed from Shipped, with the open dispute row.
        var o12 = AddOrder(9012, eva, maker3, taska, DaysAgo(6));
        Pay(o12, DaysAgo(6).AddMinutes(20));
        Accept(o12, DaysAgo(5));
        Ship(o12, DaysAgo(4), "Z9988776655");
        clock.UtcNow = DaysAgo(2);
        Ensure(o12.OpenDispute(clock), "dispute 9012");
        var dispute = Dispute.Open("seed-dispute-01", o12.Id, DisputeCategory.DamagedItem,
            "Taška dorazila s roztrženým švem u ucha, posílám fotky. Chtěla bych výměnu nebo vrácení peněz.",
            DisputeSource.Customer, Country);
        dispute.MarkCreated(Actor, DaysAgo(2));
        db.Add(dispute);

        // 9013: PersonalPickup order, Delivered + 3★ review.
        var o13 = AddOrder(9013, jana, maker4, prkenko, DaysAgo(7), ShippingMethod.PersonalPickup);
        Pay(o13, DaysAgo(7).AddMinutes(20));
        Accept(o13, DaysAgo(6));
        clock.UtcNow = DaysAgo(4);
        Ensure(o13.Ship(clock, shippingCarrierRef: null, AutoDeliverWindowDays), "ship 9013");
        Deliver(o13, DaysAgo(3));
        AddReview("seed-review-04", o13, jana, rating: 3,
            "Prkénko hezké, ale osobní odběr se dvakrát přesouval.", DaysAgo(2));

        // 9014: custom order (no product), Accepted.
        var o14 = AddOrder(9014, petr, maker1, product: null, DaysAgo(4),
            productPriceMinor: 120_000,
            notes: "Vlastní STL – držák na mikrofon, průměr ramene 48 mm. Soubor pošlu zprávou.");
        Pay(o14, DaysAgo(4).AddHours(2));
        Accept(o14, DaysAgo(3));

        // --- Denormalized maker catalog stats (recompute-from-rows) ---
        foreach (var maker in _makers)
        {
            var makerReviews = _reviews.Where(r => r.MakerId == maker.Id).ToList();
            var averageBp = makerReviews.Count == 0
                ? 0
                : (int)Math.Round(makerReviews.Average(r => (double)r.Rating) * 10_000, MidpointRounding.AwayFromZero);
            var completedOrders = _orders.Count(o => o.MakerId == maker.Id && o.State == OrderState.Completed);
            maker.SetCatalogStats(averageBp, makerReviews.Count, completedOrders);
        }
    }

    // === Builders ===

    private User AddUser(
        string id, string email, UserRole role, string fullName,
        string passwordHash, bool confirmed, DateTimeOffset createdAt)
    {
        var user = User.Create(id, email, role, fullName, Country, passwordHash,
            emailAlreadyConfirmed: confirmed, confirmedAt: confirmed ? createdAt : null);
        user.MarkCreated(Actor, createdAt);
        db.Add(user);
        return user;
    }

    private Maker AddMaker(
        string id, User user, string ico, string? vatId, string company, string slug,
        string street, string houseNumber, string city, string zip,
        string bio, string? bank, bool pickup, string? pickupNote,
        bool verified, DateTimeOffset createdAt, string[] categories)
    {
        var address = Address.Create($"{id}-addr", street, houseNumber, city, zip,
            countryCodeIso: Country, auditCountryCode: Country);
        address.MarkCreated(Actor, createdAt);
        db.Add(address);

        var maker = Maker.Create(id, user.Id, ico, vatId, company,
            legalForm: "Společnost s ručením omezeným",
            registeredAddressId: address.Id,
            incorporatedOn: DateOnly.FromDateTime(createdAt.UtcDateTime.AddYears(-3)),
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: createdAt,
            snapshotIsStale: false,
            countryCode: Country,
            slug: slug);
        maker.UpdateProfile(bio, bank, pickup, pickupNote);
        if (verified)
        {
            maker.MarkVerified();
        }
        maker.MarkCreated(Actor, createdAt);
        db.Add(maker);
        _makers.Add(maker);

        foreach (var categoryId in categories)
        {
            db.Add(MakerCategory.Link(maker.Id, categoryId, Country, createdAt));
        }

        return maker;
    }

    private Product AddProduct(
        string id, Maker maker, string categoryId, string title, string description,
        long priceMinor, PriceType priceType, FulfillmentType fulfillmentType,
        int weightGrams, DateTimeOffset createdAt)
    {
        var product = Product.Create(id, maker.Id, categoryId, title, description,
            new Makables.Core.Domain.Money.Money(priceMinor, Currency), priceType, weightGrams, Country, fulfillmentType);
        product.MarkCreated(Actor, createdAt);
        db.Add(product);
        return product;
    }

    private Order AddOrder(
        int number, User customer, Maker maker, Product? product, DateTimeOffset createdAt,
        ShippingMethod shippingMethod = ShippingMethod.ZasilkovnaPickupPoint,
        long? productPriceMinor = null, string? notes = null)
    {
        var productMinor = productPriceMinor
            ?? product?.PriceAmountMinor
            ?? throw new InvalidOperationException("Custom orders must supply productPriceMinor.");
        var shippingMinor = shippingMethod == ShippingMethod.ZasilkovnaPickupPoint ? ZasilkovnaShippingMinor : 0L;
        var totalMinor = productMinor + shippingMinor;
        // Half-up 15% platform fee, mirroring OrderPricing's rounding policy.
        var feeMinor = (totalMinor * PlatformFeeRateBp + 5_000) / 10_000;

        var order = Order.Create(
            id: $"seed-order-{number - 9000:D2}",
            orderNumber: $"M-CZ-{createdAt.Year:D4}{number:D4}",
            customerUserId: customer.Id,
            makerId: maker.Id,
            productId: product?.Id,
            contactName: customer.FullName,
            contactEmail: customer.Email,
            contactPhone: "+420 601 234 567",
            productPriceAmountMinor: productMinor,
            shippingPriceAmountMinor: shippingMinor,
            platformFeeAmountMinor: feeMinor,
            makerPayoutAmountMinor: totalMinor - feeMinor,
            totalAmountMinor: totalMinor,
            currency: Currency,
            vatRateBp: VatRateBp,
            shippingMethod: shippingMethod,
            zasilkovnaPickupPointId: shippingMethod == ShippingMethod.ZasilkovnaPickupPoint ? "1234" : null,
            countryCode: Country,
            customerNotes: notes);
        order.MarkCreated(Actor, createdAt);
        db.Add(order);
        _orders.Add(order);
        return order;
    }

    private void Pay(Order order, DateTimeOffset at)
    {
        clock.UtcNow = at;
        Ensure(order.MarkAsPaid(clock, $"SEED-PAY-{order.OrderNumber[^4..]}", "CARD_CZ"), $"pay {order.OrderNumber}");
    }

    private void Accept(Order order, DateTimeOffset at)
    {
        clock.UtcNow = at;
        Ensure(order.Accept(clock), $"accept {order.OrderNumber}");
    }

    private void Ship(Order order, DateTimeOffset at, string carrierRef)
    {
        clock.UtcNow = at;
        Ensure(order.Ship(clock, carrierRef, AutoDeliverWindowDays,
            $"https://tracking.packeta.com/{carrierRef}"), $"ship {order.OrderNumber}");
    }

    private void Deliver(Order order, DateTimeOffset at)
    {
        clock.UtcNow = at;
        Ensure(order.MarkAsDelivered(clock, OrderDeliverySource.Customer), $"deliver {order.OrderNumber}");
    }

    private void RunToCompleted(
        Order order, DateTimeOffset paidAt, DateTimeOffset acceptedAt,
        DateTimeOffset shippedAt, string carrierRef, DateTimeOffset deliveredAt, DateTimeOffset completedAt)
    {
        Pay(order, paidAt);
        Accept(order, acceptedAt);
        Ship(order, shippedAt, carrierRef);
        Deliver(order, deliveredAt);
        clock.UtcNow = completedAt;
        Ensure(order.Complete(clock), $"complete {order.OrderNumber}");
    }

    private void AddMessage(
        Order order, OrderMessageAuthorRole authorRole, User author,
        string body, DateTimeOffset at, bool unread)
    {
        var message = OrderMessage.Create($"seed-msg-{++_messageCount:D2}", order.Id, authorRole, author.Id, body, Country);
        message.MarkCreated(Actor, at);
        db.Add(message);
        if (unread)
        {
            order.IncrementUnreadFor(authorRole);
        }
    }

    private Review AddReview(string id, Order order, User customer, short rating, string body, DateTimeOffset at)
    {
        var review = Review.Create(id, order.Id, order.MakerId, customer.Id, rating, body, Country);
        review.MarkCreated(Actor, at);
        db.Add(review);
        _reviews.Add(review);
        return review;
    }

    private DateTimeOffset DaysAgo(double days) => _now.AddDays(-days);

    private DateTimeOffset HoursAgo(double hours) => _now.AddHours(-hours);

    private static void Ensure(BusinessResult result, string step)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Seed state transition failed at '{step}'.");
        }
    }

    /// <summary>Category ids seeded by the Categories migration (T-0040).</summary>
    private static class SeedCategories
    {
        public const string Print3d = "cat-3d-tisk";
        public const string ClassicPrint = "cat-klasicky-tisk";
        public const string TextilePrint = "cat-potisk-textilu";
        public const string LaserCnc = "cat-laser-cnc";
        public const string LargeFormat = "cat-velkoformat";
        public const string Handmade = "cat-handmade";
    }
}
