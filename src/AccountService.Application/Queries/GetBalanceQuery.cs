using AccountService.Application.Abstractions;
using MediatR;

namespace AccountService.Application.Queries;

public sealed record GetBalanceQuery(string AccountId) : IRequest<decimal>;

public sealed class GetBalanceQueryHandler(IAccountRepository accountRepository) : IRequestHandler<GetBalanceQuery, decimal>
{
    public async Task<decimal> Handle(GetBalanceQuery request, CancellationToken cancellationToken) =>
        await accountRepository.GetBalanceAsync(request.AccountId, cancellationToken);
}
