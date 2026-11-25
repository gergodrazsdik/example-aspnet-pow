using System.Collections.Concurrent;

namespace PowForm.Web;

/// <summary>
/// Remembers the challenges that were answered, so an answer works once. This is the only state the
/// proof of work keeps, and only after use: issuing a challenge stores nothing.
/// </summary>
public interface IUsedChallenges
{
    /// <summary>Marks a challenge used.</summary>
    /// <param name="challenge">The challenge's hash: unique per challenge, because the salt is random.</param>
    /// <param name="expires">When the challenge expires; after that the entry is not needed, because the answer is refused anyway.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>True the first time, false when the challenge was used before.</returns>
    Task<bool> TryUseAsync(string challenge, DateTimeOffset expires, CancellationToken cancellationToken = default);
}

/// <summary>
/// The in-memory store: fine for one instance. With several replicas, an answer used on one is fresh
/// on the others, so they need a shared store behind <see cref="IUsedChallenges"/> (see README).
/// </summary>
public sealed class InMemoryUsedChallenges(TimeProvider time) : IUsedChallenges
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    // An entry outlives its challenge's expiry by this much. A caller that checked the expiry a moment
    // before it passed (or a replica whose clock runs a little behind) can still be about to use the
    // challenge; if a sweep removed the first use's entry in that window, the replay would be accepted.
    // A shared store needs the same margin: its TTL must be longer than the expiry, not equal to it.
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);
    private long _nextSweepTicks;

    /// <summary>How many entries are kept.</summary>
    public int Count => _used.Count;

    public Task<bool> TryUseAsync(string challenge, DateTimeOffset expires, CancellationToken cancellationToken = default)
    {
        // An already expired challenge is refused and not stored: its answer fails the expiry check anyway.
        if (expires < time.GetUtcNow())
        {
            return Task.FromResult(false);
        }

        Sweep();
        return Task.FromResult(_used.TryAdd(challenge, expires));
    }

    // Drops entries that expired more than Grace ago, at most once a minute, on use: no timer, no background service.
    private void Sweep()
    {
        var now = time.GetUtcNow();
        var due = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < due || Interlocked.CompareExchange(ref _nextSweepTicks, (now + SweepEvery).UtcTicks, due) != due)
        {
            return;
        }

        foreach (var (key, expires) in _used)
        {
            if (expires + Grace < now)
            {
                _used.TryRemove(key, out _);
            }
        }
    }
}
