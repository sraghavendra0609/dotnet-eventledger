using EventGateway.Domain.Enums;

namespace EventGateway.Application.Abstractions;

public interface IAccountClient
{
    Task ApplyTransactionAsync(string accountId, Guid eventId, EventType eventType, decimal amount, DateTimeOffset eventTimestamp, CancellationToken cancellationToken);
}
