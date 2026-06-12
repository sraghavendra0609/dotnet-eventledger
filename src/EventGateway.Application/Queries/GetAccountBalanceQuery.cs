using EventGateway.Application.Abstractions;
using MediatR;

namespace EventGateway.Application.Queries;

public sealed record GetAccountBalanceQuery(string AccountId) : IRequest<decimal>;

public sealed class GetAccountBalanceQueryHandler(IAccountClient accountClient) : IRequestHandler<GetAccountBalanceQuery, decimal>
{
    public Task<decimal> Handle(GetAccountBalanceQuery request, CancellationToken cancellationToken) =>
        accountClient.GetBalanceAsync(request.AccountId, cancellationToken);
}
