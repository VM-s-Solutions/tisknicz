namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Per ADR 0014 / patterns §A.14 retry classification. An event with
/// <see cref="Permanent"/> or <see cref="Configuration"/> kind is not
/// retried; admin sees it as stalled and either retries manually or
/// acknowledges.
/// </summary>
public enum OutboxErrorKind
{
    None = 0,
    Transient = 1,
    Permanent = 2,
    Configuration = 3,
    Unknown = 4,
}
