using AccountService.Domain.Entities;

namespace AccountService.Application.Abstractions;

public interface IAccountRepository
{
    Task<AccountTransaction?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken);
    Task AddAsync(AccountTransaction accountTransaction, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountTransaction>> GetByAccountAsync(string accountId, CancellationToken cancellationToken);
    Task<decimal> GetBalanceAsync(string accountId, CancellationToken cancellationToken);
}
