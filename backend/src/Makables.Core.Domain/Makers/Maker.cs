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
        string countryCode)
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
            if (trimmed.Length > 500)
                throw new ArgumentException("Bio must be 500 chars or fewer.", nameof(bio));
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
}
