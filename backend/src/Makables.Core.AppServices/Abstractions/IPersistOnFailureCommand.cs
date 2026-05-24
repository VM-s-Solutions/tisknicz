namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Marker for commands whose state mutations MUST persist even when the
/// handler returns a <see cref="Makables.Core.Domain.Common.BusinessResult"/>
/// failure.
///
/// The default Unit-of-Work pipeline behavior commits only on success
/// (handlers that bail early should leave the DbContext untouched). But
/// the anti-abuse and security paths in <c>Features/Auth/*</c> need the
/// opposite contract:
///   - Login.Handler: failed-attempt counters on User + LoginAttemptBucket
///     MUST persist or the 5-attempt lockout in ADR 0012 §Lockout never
///     fires (reviewer T-0022 BLOCKER B-1).
///   - Refresh.Handler: family-wide revocation on reuse detection MUST
///     persist or the stolen-token replay defense in ADR 0012 §Refresh
///     token is a no-op.
///   - Logout.Handler: the revoke MUST persist or logout is silently
///     ignored.
///
/// A handler that opts in MUST treat the DbContext as committed even on
/// failure — no "decide to bail after touching state" pattern.
/// </summary>
public interface IPersistOnFailureCommand : ICommandMarker { }
