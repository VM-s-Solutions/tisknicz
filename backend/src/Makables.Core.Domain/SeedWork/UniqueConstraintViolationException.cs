namespace Makables.Core.Domain.SeedWork;

/// <summary>
/// Raised by <see cref="IUnitOfWork.SaveChangesAsync"/> when the
/// database rejects a row insert/update because it would violate a
/// unique-index constraint (Postgres SQLSTATE <c>23505</c>).
///
/// <para>
/// This exists so the <c>UnitOfWorkPipelineBehavior</c> can translate
/// concurrent-write races into a typed <c>BusinessResult</c> failure
/// instead of bubbling a raw <c>DbUpdateException</c> as a 500. The
/// classic case: two registrations of the same email/IČO both pass the
/// application-side pre-check, then the loser hits the partial unique
/// index. Without this exception the loser sees a 500; with it, the
/// loser sees the same <c>Conflict</c> as if the pre-check had won the
/// race. T-0033 reviewer security M-1.
/// </para>
///
/// <para>
/// <see cref="ConstraintName"/> is the database-level index name
/// (e.g. <c>ix_makers_registration_number</c>). The
/// <see cref="IUniqueConstraintTranslator"/> in
/// <c>Core.AppServices</c> maps it to a <c>BusinessErrorMessage</c>.
/// </para>
/// </summary>
public sealed class UniqueConstraintViolationException(string constraintName, Exception inner)
    : Exception($"Unique constraint '{constraintName}' violated.", inner)
{
    public string ConstraintName { get; } = constraintName;
}
