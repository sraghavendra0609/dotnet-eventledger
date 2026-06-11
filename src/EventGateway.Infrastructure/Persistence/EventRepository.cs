using EventGateway.Application.Abstractions;
using EventGateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventGateway.Infrastructure.Persistence;

public sealed class EventRepository(EventGatewayDbContext dbContext) : IEventRepository
{
    public async Task<EventRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken) =>
        await dbContext.Events.SingleOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public async Task<EventRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Events.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventRecord>> GetByAccountAsync(string accountId, CancellationToken cancellationToken) =>
        await dbContext.Events.Where(x => x.AccountId == accountId).OrderBy(x => x.EventTimestamp).ToListAsync(cancellationToken);

    public async Task AddAsync(EventRecord eventRecord, CancellationToken cancellationToken)
    {
        dbContext.Events.Add(eventRecord);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
