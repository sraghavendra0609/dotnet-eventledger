using EventGateway.Domain.Entities;

namespace EventGateway.Application.Dto;

public sealed record EventDto(
    Guid Id,
    Guid EventId,
    string AccountId,
    string EventType,
    decimal Amount,
    string Currency,
    DateTimeOffset EventTimestamp,
    Dictionary<string, string>? Metadata,
    DateTimeOffset CreatedAt)
{
    public static EventDto FromEntity(EventRecord eventRecord) =>
        new(
            eventRecord.Id,
            eventRecord.EventId,
            eventRecord.AccountId,
            eventRecord.EventType.ToString().ToUpperInvariant(),
            eventRecord.Amount,
            eventRecord.Currency,
            eventRecord.EventTimestamp,
            eventRecord.Metadata,
            eventRecord.CreatedAt);
}
