using AccountService.Application.Abstractions;
using AccountService.Application.Dto;
using MediatR;

namespace AccountService.Application.Queries;

public sealed record GetAccountQuery(string AccountId) : IRequest<AccountDto>;

public sealed class GetAccountQueryHandler(IAccountRepository accountRepository) : IRequestHandler<GetAccountQuery, AccountDto>
{
    public async Task<AccountDto> Handle(GetAccountQuery request, CancellationToken cancellationToken)
    {
        var transactions = await accountRepository.GetByAccountAsync(request.AccountId, cancellationToken);
        var balance = await accountRepository.GetBalanceAsync(request.AccountId, cancellationToken);
        return new AccountDto(request.AccountId, balance, transactions.Select(AccountTransactionDto.FromEntity).ToList());
    }
}
