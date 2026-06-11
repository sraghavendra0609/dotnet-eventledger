using AccountService.Application.Abstractions;
using AccountService.Domain.Entities;
using AccountService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Infrastructure.Persistence;

public sealed class AccountRepository(AccountDbContext dbContext) : IAccountRepository
{
    public async Task<AccountTransaction?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken) =>
        await dbContext.Transactions.SingleOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public async Task AddAsync(AccountTransaction accountTransaction, CancellationToken cancellationToken)
    {
        dbContext.Transactions.Add(accountTransaction);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountTransaction>> GetByAccountAsync(string accountId, CancellationToken cancellationToken) =>
        await dbContext.Transactions.Where(x => x.AccountId == accountId).OrderBy(x => x.EventTimestamp).ToListAsync(cancellationToken);

    public async Task<decimal> GetBalanceAsync(string accountId, CancellationToken cancellationToken)
    {
        var credits = await dbContext.Transactions
            .Where(x => x.AccountId == accountId && x.EventType == EventType.Credit)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var debits = await dbContext.Transactions
            .Where(x => x.AccountId == accountId && x.EventType == EventType.Debit)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return credits - debits;
    }
}
