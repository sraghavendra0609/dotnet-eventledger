using System.Collections.Concurrent;

namespace EventGateway.Application.Services;

/// <summary>
/// Provides per-EventId serialization so that concurrent requests with the same EventId
/// cannot both observe "not exists" and proceed in parallel, preventing duplicate
/// downstream calls and duplicate persistence.
/// </summary>
public sealed class EventIdempotencyLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetLockFor(Guid eventId) =>
        _locks.GetOrAdd(eventId, _ => new SemaphoreSlim(1, 1));
}
