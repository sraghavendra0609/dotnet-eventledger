using System.Diagnostics;
using System.Diagnostics.Metrics;
using EventGateway.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EventGateway.Api.Controllers;

[ApiController]
[Route("accounts")]
public sealed class AccountsController(IMediator mediator) : ControllerBase
{
    private static readonly Meter Meter = new("EventGateway.Api");
    private static readonly Counter<long> RequestsCounter = Meter.CreateCounter<long>("event_gateway_requests_total");

    [HttpGet("{accountId}/balance")]
    public async Task<IActionResult> GetBalance(string accountId, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "GET /accounts/{accountId}/balance" } });
        var balance = await mediator.Send(new GetAccountBalanceQuery(accountId), cancellationToken);
        return Ok(new { accountId, balance });
    }
}
