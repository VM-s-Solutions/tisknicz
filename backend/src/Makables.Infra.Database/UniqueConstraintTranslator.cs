using Makables.Core.Domain.Common;
using Makables.Core.Domain.SeedWork;

namespace Makables.Infra.Database;

/// <summary>
/// Maps the partial-unique index names declared in
/// <c>Makables.Infra.Database/Configurations/</c> to their domain
/// <see cref="Error"/> codes. Driven by
/// <c>UnitOfWorkPipelineBehavior</c> when a concurrent write loses the
/// race at <c>SaveChangesAsync</c>. T-0033 reviewer security M-1.
///
/// <para>
/// Add an entry here ONLY for unique indexes where a concurrent-write
/// 23505 should surface as a typed, user-facing <see cref="Error"/> —
/// in practice this means constraints that an application-level
/// pre-check already guards (e.g. <c>EmailExistsAsync</c>,
/// <c>IcoExistsAsync</c>), so a 23505 here is the loser of a TOCTOU
/// race and the correct response is the same Conflict the pre-check
/// would have returned.
/// </para>
///
/// <para>
/// Constraints without a mapping return <c>null</c> — the pipeline
/// rethrows the underlying exception so unexpected violations remain
/// visible. Defence-in-depth invariants (e.g. <c>ix_makers_user_id</c>
/// gating one Maker row per User: the handler already adds at most one,
/// so a 23505 here would mean an unexpected concurrent insert that the
/// handler couldn't have produced) MUST stay unmapped — translating
/// them to a generic conflict masks a real bug. T-0033 Copilot review.
/// </para>
/// </summary>
public sealed class UniqueConstraintTranslator : IUniqueConstraintTranslator
{
    private static readonly IReadOnlyDictionary<string, Error> Mappings =
        new Dictionary<string, Error>(StringComparer.OrdinalIgnoreCase)
        {
            // Customer + maker registration both pre-check email
            // uniqueness; the race-losing insert surfaces here.
            ["IX_users_email_normalized"] =
                Error.Conflict("email", BusinessErrorMessage.AuthEmailAlreadyExists),

            // RegisterMaker pre-checks IcoExistsAsync; the race-losing
            // insert surfaces here.
            ["ix_makers_registration_number"] =
                Error.Conflict("registrationNumber", BusinessErrorMessage.MakerIcoAlreadyRegistered),

            // Intentionally unmapped (T-0033 Copilot review):
            //   ix_makers_user_id — one Maker row per User. The handler
            //   adds exactly one Maker per RegisterMaker call, so a 23505
            //   on this constraint means an unexpected concurrent insert
            //   we couldn't have produced. Let it rethrow so the
            //   underlying bug stays visible.
        };

    public Error? Translate(string constraintName) =>
        Mappings.TryGetValue(constraintName, out var error) ? error : null;
}
