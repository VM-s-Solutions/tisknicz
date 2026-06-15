namespace Makables.Core.Domain.Outbox;

/// <summary>
/// One stalled-outbox triage row (T-0127 / Q-0029 / US-admin-0014). The
/// browsable list backing the outbox triage page — retry / acknowledge by
/// VISIBLE id instead of the T-0118c count + blind by-id surface. Carries
/// only the columns the triage page renders (no <c>PayloadJson</c> — a
/// detail concern).
///
/// <para>
/// The list is filtered by the EXACT stalled predicate
/// <see cref="IOutboxConsumerRepository.StalledPredicate"/> shares with
/// <see cref="IOutboxConsumerRepository.CountStalledAsync"/>, so the list
/// and the KPI tile can never disagree on which rows are stalled.
/// </para>
/// </summary>
public sealed record StalledOutboxEventDto(
    string Id,
    string EventType,
    string AggregateId,
    string? LastErrorCode,
    int RetryCount,
    DateTimeOffset CreatedAt);
