using EventGateway.Domain.Enums;

namespace EventGateway.Domain.Entities;

public class EventRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public EventType EventType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset EventTimestamp { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
