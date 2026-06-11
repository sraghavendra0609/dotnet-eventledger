using EventGateway.Domain.Entities;

namespace EventGateway.Application.Dto;

public sealed record EventDto(
    Guid Id,
    Guid EventId,
    string AccountId,
    string EventType,
    decimal Amount,
    DateTimeOffset EventTimestamp,
    DateTimeOffset CreatedAt)
{
    public static EventDto FromEntity(EventRecord eventRecord) =>
        new(
            eventRecord.Id,
            eventRecord.EventId,
            eventRecord.AccountId,
            eventRecord.EventType.ToString().ToUpperInvariant(),
            eventRecord.Amount,
            eventRecord.EventTimestamp,
            eventRecord.CreatedAt);
}
