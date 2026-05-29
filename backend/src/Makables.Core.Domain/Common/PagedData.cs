namespace Makables.Core.Domain.Common;

/// <summary>
/// A single page of results plus the metadata a client needs to render
/// pagination controls. The first paginated read in the codebase
/// (T-0043 catalog); reused by every later list endpoint.
///
/// <para>
/// <see cref="Items"/> is the page; <see cref="TotalCount"/> is the
/// full filtered-set size (so the client can compute total pages and
/// show "X of Y"). <see cref="Page"/> is 1-based.
/// </para>
/// </summary>
public sealed record PagedData<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedData<T> Empty(int page, int pageSize) =>
        new(Array.Empty<T>(), page, pageSize, 0);
}
