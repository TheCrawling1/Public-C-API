using System.Collections.Concurrent;

namespace ApiRouter.Rules;

public interface IRateLimiter
{
    /// <summary>
    /// Records a hit for <paramref name="key"/> and returns false if it would
    /// exceed <paramref name="maxPerMinute"/> within the trailing 60-second window.
    /// </summary>
    bool TryAcquire(string key, int maxPerMinute);
}

/// <summary>
/// Process-local sliding-window rate limiter. Adequate for a single-instance
/// portfolio service; a distributed deployment would back this with Redis.
/// </summary>
public class InMemoryRateLimiter : IRateLimiter
{
    private const int PruneThreshold = 1024;
    private static readonly long PruneIntervalTicks = TimeSpan.FromSeconds(30).Ticks;

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();
    private long _lastPruneTicks = DateTime.UtcNow.Ticks;

    public bool TryAcquire(string key, int maxPerMinute)
    {
        if (maxPerMinute <= 0)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var window = now.AddMinutes(-1);

        MaybePrune(now, window);

        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= maxPerMinute)
            {
                return false;
            }

            queue.Enqueue(now);

            // If a concurrent prune removed this key while we waited on the lock, restore it
            // so the hit we just recorded isn't silently dropped.
            _hits.TryAdd(key, queue);
            return true;
        }
    }

    // Bounded, time-gated pruning: only one thread prunes, at most once per interval, so the
    // hot path stays O(1) even when many distinct keys have active (non-empty) queues.
    private void MaybePrune(DateTime now, DateTime window)
    {
        if (_hits.Count <= PruneThreshold)
        {
            return;
        }

        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now.Ticks - last < PruneIntervalTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) != last)
        {
            return; // another thread just claimed this prune slot
        }

        foreach (var pair in _hits)
        {
            var queue = pair.Value;
            lock (queue)
            {
                while (queue.Count > 0 && queue.Peek() < window)
                {
                    queue.Dequeue();
                }

                if (queue.Count == 0)
                {
                    // Remove only if this exact (now-empty) queue is still mapped.
                    ((ICollection<KeyValuePair<string, Queue<DateTime>>>)_hits).Remove(pair);
                }
            }
        }
    }
}
