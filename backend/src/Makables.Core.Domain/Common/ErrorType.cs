namespace Makables.Core.Domain.Common;

/// <summary>
/// Classification of an <see cref="Error"/>. Drives HTTP status mapping
/// (request-level kinds) and retry policy (integration-level kinds).
/// Per ADR 0002 and patterns §A.4.
/// </summary>
public enum ErrorType
{
    // Request-level — map to HTTP status codes in MakablesApiController.HandleResult.
    Validation,    // 400
    Unauthorized,  // 401
    Forbidden,     // 403
    NotFound,      // 404
    Conflict,      // 409

    // Integration-level — drive retry decisions in adapters / background jobs.
    Transient,     // 503 — retry with backoff
    Permanent,     // 422 — escalate; do not retry
    Configuration, // 500 — alert ops
    Unknown        // 500 — limited retry, then escalate
}
