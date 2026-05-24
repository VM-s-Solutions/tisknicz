namespace Makables.Core.Domain.Identity;

/// <summary>
/// Persistence boundary for <see cref="LoginAttemptBucket"/>. The auth
/// service uses one bucket per normalized email; reads + writes happen in
/// lock-step with the per-attempt cycle.
/// </summary>
public interface ILoginAttemptBucketRepository
{
    /// <summary>
    /// Resolve the bucket for <paramref name="emailNormalized"/>, or null
    /// if none exists yet. Tracked so subsequent mutations + UoW commit
    /// work naturally.
    /// </summary>
    Task<LoginAttemptBucket?> GetAsync(string emailNormalized, CancellationToken cancellationToken);

    void Add(LoginAttemptBucket bucket);
}
