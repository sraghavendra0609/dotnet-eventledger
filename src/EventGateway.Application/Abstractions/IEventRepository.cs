using EventGateway.Domain.Entities;

namespace EventGateway.Application.Abstractions;

public interface IEventRepository
{
    Task<EventRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken);
    Task<EventRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventRecord>> GetByAccountAsync(string accountId, CancellationToken cancellationToken);
    Task AddAsync(EventRecord eventRecord, CancellationToken cancellationToken);
}
