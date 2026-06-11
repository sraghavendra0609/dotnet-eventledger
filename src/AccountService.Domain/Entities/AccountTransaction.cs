using AccountService.Domain.Enums;

namespace AccountService.Domain.Entities;

public class AccountTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public EventType EventType { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset EventTimestamp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
