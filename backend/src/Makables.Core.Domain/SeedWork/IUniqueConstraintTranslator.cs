using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.SeedWork;

/// <summary>
/// Maps a database unique-constraint name to a domain <see cref="Error"/>
/// so the <c>UnitOfWorkPipelineBehavior</c> can translate a concurrent
/// unique-violation race (Postgres SQLSTATE <c>23505</c>) into a typed
/// <c>BusinessResult</c> failure.
///
/// <para>
/// Each known partial-unique index (e.g. <c>ix_makers_registration_number</c>
/// → <see cref="BusinessErrorMessage.MakerIcoAlreadyRegistered"/>) is
/// declared by the infra-layer implementation. Constraints that aren't
/// in the table return <c>null</c> — the pipeline rethrows so the
/// failure remains visible (a brand-new index that nobody mapped is a
/// bug worth surfacing). T-0033 reviewer security M-1.
/// </para>
/// </summary>
public interface IUniqueConstraintTranslator
{
    Error? Translate(string constraintName);
}
