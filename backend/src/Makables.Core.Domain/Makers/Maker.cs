using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Makers;

/// <summary>
/// A platform-side maker (a business that sells via Makables). One
/// <see cref="Maker"/> per <see cref="User"/> with <see cref="Identity.UserRole.Maker"/>.
///
/// <para>
/// <b>Snapshot semantics</b> per ADR 0018 §"Persist CompanyRecord directly":
/// the company-name / IČO / DIČ / legal-form / IsActiveInRegistry fields
/// are SNAPSHOTS captured at registration. They drive invoices, which
/// must legally carry the data AS IT WAS when the transaction happened.
/// They are NOT auto-refreshed from ARES; an admin
/// <c>RefreshMakerFromAres</c> command (T-0034) updates them deliberately.
/// </para>
///
/// <para>
/// <b>Three state flags</b>:
/// <list type="bullet">
///   <item><description><see cref="IsActiveInRegistry"/> — ARES snapshot. Was the
///     entity trading on the registry at registration / last refresh.</description></item>
///   <item><description><see cref="IsVerified"/> — admin gate. Toggled by T-0034
///     <c>VerifyMaker</c>. Until verified, products stay invisible in
///     the public catalog.</description></item>
///   <item><description><see cref="Common.Auditable.IsActive"/> — soft-delete. Toggled by
///     T-0034 <c>DeactivateMaker</c> (the soft-delete pattern from
///     <c>Auditable.MarkDeactivated</c>).</description></item>
/// </list>
/// Email confirmation gates product visibility too (per ADR 0012
/// §"Email confirmation"), enforced via the linked <see cref="UserId"/>'s
/// <c>EmailConfirmedAt</c> at catalog-query time.
/// </para>
/// </summary>
public sealed class Maker : Auditable
{
    /// <summary>Matches the <c>slug</c> column width in MakerConfiguration.</summary>
    public const int MaxSlugLength = 120;

    /// <summary>FK to <c>User</c>. Unique — one Maker row per user.</summary>
    public string UserId { get; private set; } = default!;

    /// <summary>Czech IČO (8 digits). Unique across active makers.</summary>
    public string RegistrationNumber { get; private set; } = default!;

    /// <summary>Czech DIČ (VAT id) — null if the maker isn't VAT-registered.</summary>
    public string? VatId { get; private set; }

    /// <summary>Snapshot of the company's legal name at registration time.</summary>
    public string CompanyName { get; private set; } = default!;

    /// <summary>Snapshot of the legal form (e.g. "Společnost s ručením omezeným").</summary>
    public string? LegalForm { get; private set; }

    /// <summary>FK to <c>Address</c> (the legal seat from ARES).</summary>
    public string RegisteredAddressId { get; private set; } = default!;

    public DateOnly? IncorporatedOn { get; private set; }

    /// <summary>
    /// Was the company trading per the registry at registration / last
    /// refresh. Read-only outside the registration / admin-refresh paths.
    /// </summary>
    public bool IsActiveInRegistry { get; private set; }

    /// <summary>
    /// Admin verification gate. Default <c>false</c> on registration;
    /// flipped to <c>true</c> by an admin (T-0034 <c>VerifyMaker</c>).
    /// </summary>
    public bool IsVerified { get; private set; }

    /// <summary>Which registry the snapshot came from (e.g. "ares").</summary>
    public string SourceRegistry { get; private set; } = default!;

    /// <summary>When the snapshot was fetched (memo of ARES <c>FetchedAt</c>).</summary>
    public DateTimeOffset SnapshotFetchedAt { get; private set; }

    /// <summary>
    /// True when the snapshot was served from the 7-day stale-fallback
    /// path (ADR 0018) rather than a fresh ARES fetch. T-0034 admin UI
    /// can prioritise refreshing these rows.
    /// </summary>
    public bool SnapshotIsStale { get; private set; }

    // === Maker-editable profile fields (T-0034) ===

    /// <summary>
    /// Free-text bio shown on the public maker profile. Max 500 chars
    /// per US-maker-0003 AC-1. Null until the maker fills it in.
    /// </summary>
    public string? Bio { get; private set; }

    /// <summary>
    /// Czech bank account in <c>123456789/0100</c> format. Validated
    /// by <c>CzechBankAccountValidator</c> at command time. Required
    /// for payouts; null until the maker provides it.
    /// </summary>
    public string? BankAccount { get; private set; }

    /// <summary>
    /// True when the maker offers personal pickup (US-maker-0015).
    /// Defaults to false on registration. The pickup address itself
    /// is wired in a follow-up ticket (T-0034 only ships the toggle +
    /// note; pickup-address management lives with the address-graph
    /// work).
    /// </summary>
    public bool PersonalPickupEnabled { get; private set; }

    /// <summary>Free-text pickup instructions shown to customers (e.g. opening hours, doorbell name).</summary>
    public string? PickupNote { get; private set; }

    // === Catalog fields (T-0043) ===

    /// <summary>
    /// URL slug for the public maker profile (<c>/katalog/{slug}</c>),
    /// derived from <see cref="CompanyName"/> at registration. Unique
    /// across active makers. Stable across company-name refreshes from
    /// ARES — <see cref="UpdateSnapshot"/> does NOT change it (a public
    /// URL that silently changes breaks every external link).
    /// </summary>
    public string Slug { get; private set; } = default!;

    /// <summary>
    /// Denormalized average review rating in basis points of a star
    /// (0..50000 = 0.0..5.0 stars), kept so the catalog sort doesn't
    /// aggregate reviews per query. Populated by the review-creation
    /// flow (T-0050); 0 until the maker has reviews.
    /// </summary>
    public int RatingAverageBp { get; private set; }

    /// <summary>Number of reviews backing <see cref="RatingAverageBp"/>. 0 until reviewed.</summary>
    public int RatingCount { get; private set; }

    /// <summary>
    /// Denormalized count of completed orders, the secondary catalog
    /// sort key (US-customer-0007 AC-1). Maintained by the order-
    /// completion flow (future ticket); 0 at registration.
    /// </summary>
    public int TotalOrders { get; private set; }

    /// <summary>
    /// True when this maker row has been ANONYMIZED-BUT-LEGALLY-RETAINED by
    /// a GDPR erasure (T-0110). The PII fields are scrubbed to
    /// <c>"Anonymized"</c> but <see cref="RegistrationNumber"/> (IČO) +
    /// <see cref="BankAccount"/> are retained because tax records (invoices,
    /// payout batches) reference them. Default <c>false</c>; flipped to
    /// <c>true</c> by <see cref="AnonymizeForErasure"/>. Lets active-maker
    /// surfaces exclude erased tombstones.
    /// </summary>
    public bool IsRetainedForLegal { get; private set; }

    private Maker() { }

    public static Maker Create(
        string id,
        string userId,
        string registrationNumber,
        string? vatId,
        string companyName,
        string? legalForm,
        string registeredAddressId,
        DateOnly? incorporatedOn,
        bool isActiveInRegistry,
        string sourceRegistry,
        DateTimeOffset snapshotFetchedAt,
        bool snapshotIsStale,
        string countryCode,
        string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(registrationNumber))
            throw new ArgumentException("RegistrationNumber is required.", nameof(registrationNumber));
        if (string.IsNullOrWhiteSpace(companyName))
            throw new ArgumentException("CompanyName is required.", nameof(companyName));
        if (string.IsNullOrWhiteSpace(registeredAddressId))
            throw new ArgumentException("RegisteredAddressId is required.", nameof(registeredAddressId));
        if (string.IsNullOrWhiteSpace(sourceRegistry))
            throw new ArgumentException("SourceRegistry is required.", nameof(sourceRegistry));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        // Slug: caller may supply one (RegisterMaker disambiguates
        // collisions by appending the IČO); otherwise derive from the
        // company name. A company name like "***" slugifies to empty —
        // fall back to the IČO so we always have a usable URL segment.
        // Both the derived and the override path are bounded to
        // MaxSlugLength so a long ARES name can't produce a slug that
        // passes validation but overflows the column (T-0043 Copilot
        // review).
        var resolvedSlug = string.IsNullOrWhiteSpace(slug)
            ? SlugGenerator.Slugify(companyName, MaxSlugLength)
            : slug.Trim();
        if (resolvedSlug.Length == 0)
            resolvedSlug = registrationNumber.Trim();
        if (resolvedSlug.Length > MaxSlugLength)
            throw new ArgumentException($"Slug must be at most {MaxSlugLength} chars.", nameof(slug));
        if (!SlugGenerator.IsValid(resolvedSlug))
            throw new ArgumentException("Slug must match [a-z0-9-]+ (no leading/trailing/double dashes).", nameof(slug));

        return new Maker
        {
            Id = id,
            UserId = userId,
            RegistrationNumber = registrationNumber.Trim(),
            VatId = string.IsNullOrWhiteSpace(vatId) ? null : vatId.Trim(),
            CompanyName = companyName.Trim(),
            LegalForm = string.IsNullOrWhiteSpace(legalForm) ? null : legalForm.Trim(),
            RegisteredAddressId = registeredAddressId,
            IncorporatedOn = incorporatedOn,
            IsActiveInRegistry = isActiveInRegistry,
            IsVerified = false,
            SourceRegistry = sourceRegistry.ToLowerInvariant(),
            SnapshotFetchedAt = snapshotFetchedAt,
            SnapshotIsStale = snapshotIsStale,
            CountryCode = countryCode.ToUpperInvariant(),
            Slug = resolvedSlug,
            RatingAverageBp = 0,
            RatingCount = 0,
            TotalOrders = 0,
        };
    }

    // === Mutation methods (admin actions, T-0034) ===

    /// <summary>
    /// Admin sets the Maker as verified. Throws if already verified —
    /// the existing <see cref="BusinessErrorMessage.MakerAlreadyVerified"/>
    /// code maps to this case.
    /// </summary>
    public Maker MarkVerified()
    {
        if (IsVerified)
            throw new InvalidOperationException("Maker is already verified.");
        IsVerified = true;
        return this;
    }

    /// <summary>
    /// Refresh the denormalized catalog stats. Called by the review-
    /// creation flow (T-0050, supplies new rating average + count) and
    /// the order-completion flow (bumps total orders). T-0043 ships the
    /// fields + this setter; the producers wire it up in their tickets.
    /// </summary>
    public Maker SetCatalogStats(int ratingAverageBp, int ratingCount, int totalOrders)
    {
        if (ratingAverageBp is < 0 or > 50_000)
            throw new ArgumentOutOfRangeException(nameof(ratingAverageBp), "Rating average must be 0..50000 bp (0..5.0 stars).");
        if (ratingCount < 0)
            throw new ArgumentOutOfRangeException(nameof(ratingCount), "Rating count cannot be negative.");
        if (totalOrders < 0)
            throw new ArgumentOutOfRangeException(nameof(totalOrders), "Total orders cannot be negative.");

        RatingAverageBp = ratingAverageBp;
        RatingCount = ratingCount;
        TotalOrders = totalOrders;
        return this;
    }

    /// <summary>
    /// Recompute the denormalized rating from the review rows (T-0100,
    /// Q5 recompute-from-rows). Thin forward to <see cref="SetCatalogStats"/>
    /// that keeps <see cref="TotalOrders"/> UNTOUCHED — the order-completion
    /// flow owns that field. The caller (the <c>SubmitReview</c> handler)
    /// supplies <paramref name="ratingCount"/> + <paramref name="ratingAverageBp"/>
    /// computed by an <c>AVG(rating)</c> over the maker's ACTIVE reviews
    /// (recompute-from-rows is self-healing under soft-delete; a running
    /// average would drift permanently after any deactivation). The
    /// existing 0..50000-bp guard on <see cref="SetCatalogStats"/> applies.
    /// </summary>
    public Maker RecomputeRating(int ratingCount, int ratingAverageBp) =>
        SetCatalogStats(ratingAverageBp, ratingCount, TotalOrders);

    /// <summary>
    /// Maker self-service profile patch (T-0034 <c>UpdateMakerProfile</c>).
    /// Null arguments mean "don't change this field"; an explicit empty
    /// string clears an optional value. <see cref="BankAccount"/> format
    /// is enforced by the caller (the FluentValidation rule on the
    /// command) — the entity treats the string as opaque.
    ///
    /// <para>
    /// Does NOT touch any snapshot field, the registry flags, or
    /// <see cref="IsVerified"/>. ARES-snapshot fields are read-only for
    /// makers (US-maker-0003 AC-2: legal requirement, invoices can't
    /// change silently).
    /// </para>
    /// </summary>
    public Maker UpdateProfile(
        string? bio,
        string? bankAccount,
        bool? personalPickupEnabled,
        string? pickupNote)
    {
        if (bio is not null)
        {
            var trimmed = bio.Trim();
            // 500-char cap is enforced by UpdateMakerProfile.Validator on
            // the trimmed length; this is a defensive invariant for direct
            // domain callers (seed data, future internal use). T-0034 cq
            // reviewer M-2 — single canonical boundary in the validator,
            // entity asserts as a precondition.
            if (trimmed.Length > 500)
                throw new ArgumentException("Bio must be 500 chars or fewer (post-trim).", nameof(bio));
            Bio = trimmed.Length == 0 ? null : trimmed;
        }

        if (bankAccount is not null)
        {
            var trimmed = bankAccount.Trim();
            BankAccount = trimmed.Length == 0 ? null : trimmed;
        }

        if (personalPickupEnabled.HasValue)
        {
            PersonalPickupEnabled = personalPickupEnabled.Value;
        }

        if (pickupNote is not null)
        {
            var trimmed = pickupNote.Trim();
            PickupNote = trimmed.Length == 0 ? null : trimmed;
        }

        return this;
    }

    /// <summary>
    /// Update the ARES snapshot fields after a deliberate refresh
    /// (T-0034 <c>RefreshMakerFromAres</c>). Does NOT touch
    /// <see cref="IsVerified"/> — admin verification is independent of
    /// the registry snapshot.
    /// </summary>
    public Maker UpdateSnapshot(
        string companyName,
        string? vatId,
        string? legalForm,
        DateOnly? incorporatedOn,
        bool isActiveInRegistry,
        DateTimeOffset snapshotFetchedAt,
        bool snapshotIsStale)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            throw new ArgumentException("CompanyName is required.", nameof(companyName));
        CompanyName = companyName.Trim();
        VatId = string.IsNullOrWhiteSpace(vatId) ? null : vatId.Trim();
        LegalForm = string.IsNullOrWhiteSpace(legalForm) ? null : legalForm.Trim();
        IncorporatedOn = incorporatedOn;
        IsActiveInRegistry = isActiveInRegistry;
        SnapshotFetchedAt = snapshotFetchedAt;
        SnapshotIsStale = snapshotIsStale;
        return this;
    }

    /// <summary>
    /// GDPR erasure transform (T-0110, locked Q-A). Scrubs the maker PII to
    /// the <c>"Anonymized"</c> sentinel, RETAINS <see cref="RegistrationNumber"/>
    /// (IČO) + <see cref="BankAccount"/> (referenced by retained tax records
    /// — invoices, payout batches), and sets <see cref="IsRetainedForLegal"/>
    /// so the row is a lawful tombstone. Pure transform; idempotent (a second
    /// call leaves IČO/bank intact and the flag true). The order-completion /
    /// rating fields are NOT touched. Invoked only by the
    /// <c>IUserDataDeletionService</c> seam — the single hard-delete path.
    /// </summary>
    public Maker AnonymizeForErasure()
    {
        const string sentinel = "Anonymized";
        CompanyName = sentinel;
        LegalForm = sentinel;
        VatId = null;
        Bio = sentinel;
        PickupNote = sentinel;
        // RETAIN RegistrationNumber (IČO) + BankAccount — legal/payout records
        // reference them; scrubbing would orphan those rows (Q-A / Option I).
        IsRetainedForLegal = true;
        return this;
    }
}
