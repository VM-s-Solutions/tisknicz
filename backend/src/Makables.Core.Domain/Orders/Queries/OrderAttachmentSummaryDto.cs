namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Lightweight attachment summary embedded inline in the customer +
/// maker order-detail responses (T-0082). Shared across audiences
/// because attachment metadata (id, filename, content type, size) carries
/// no audience-specific data; the per-audience routing is baked into the
/// <see cref="DownloadUrl"/> prefix at projection time.
///
/// <para>
/// <see cref="DownloadUrl"/> is a relative path on the audience host
/// (e.g. <c>/api/v1/orders/{orderId}/attachments/{attachmentId}</c>) so
/// the frontend just appends it to its host base URL. Direct blob URLs
/// are NEVER emitted — the audience host's attachment endpoint owns the
/// IDOR check + auth boundary per T-0064.
/// </para>
/// </summary>
public sealed record OrderAttachmentSummaryDto(
    string Id,
    string Filename,
    string ContentType,
    long SizeBytes,
    string DownloadUrl);
