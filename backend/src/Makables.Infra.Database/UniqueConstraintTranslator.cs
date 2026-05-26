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
/// Add an entry here for every <c>HasIndex(...).IsUnique()</c> on an
/// entity whose insert/update is reachable from a command handler.
/// Constraints without a mapping return <c>null</c> — the pipeline
/// rethrows the underlying exception so the failure stays visible.
/// </para>
/// </summary>
public sealed class UniqueConstraintTranslator : IUniqueConstraintTranslator
{
    private static readonly IReadOnlyDictionary<string, Error> Mappings =
        new Dictionary<string, Error>(StringComparer.OrdinalIgnoreCase)
        {
            ["IX_users_email_normalized"] =
                Error.Conflict("email", BusinessErrorMessage.AuthEmailAlreadyExists),
            ["ix_makers_user_id"] =
                Error.Conflict("userId", BusinessErrorMessage.MakerIcoAlreadyRegistered),
            ["ix_makers_registration_number"] =
                Error.Conflict("registrationNumber", BusinessErrorMessage.MakerIcoAlreadyRegistered),
        };

    public Error? Translate(string constraintName) =>
        Mappings.TryGetValue(constraintName, out var error) ? error : null;
}
