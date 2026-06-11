using AccountService.Domain.Entities;

namespace AccountService.Application.Dto;

public sealed record AccountDto(string AccountId, decimal Balance, IReadOnlyList<AccountTransactionDto> Transactions);

public sealed record AccountTransactionDto(Guid EventId, string EventType, decimal Amount, DateTimeOffset EventTimestamp, DateTimeOffset CreatedAt)
{
    public static AccountTransactionDto FromEntity(AccountTransaction transaction) =>
        new(transaction.EventId, transaction.EventType.ToString().ToUpperInvariant(), transaction.Amount, transaction.EventTimestamp, transaction.CreatedAt);
}
