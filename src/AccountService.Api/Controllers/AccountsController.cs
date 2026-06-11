using System.Diagnostics;
using System.Diagnostics.Metrics;
using AccountService.Application.Commands;
using AccountService.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Api.Controllers;

[ApiController]
[Route("accounts")]
public sealed class AccountsController(IMediator mediator) : ControllerBase
{
    private static readonly Meter Meter = new("AccountService.Api");
    private static readonly Counter<long> RequestsCounter = Meter.CreateCounter<long>("account_service_requests_total");

    [HttpPost("{accountId}/transactions")]
    public async Task<IActionResult> ApplyTransaction(string accountId, [FromBody] ApplyTransactionRequest request, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "POST /accounts/{accountId}/transactions" } });
        var isDuplicate = await mediator.Send(new ApplyTransactionCommand(accountId, request.EventId, request.EventType, request.Amount, request.EventTimestamp), cancellationToken);
        return isDuplicate ? Ok(new { duplicated = true }) : Accepted(new { duplicated = false });
    }

    [HttpGet("{accountId}/balance")]
    public async Task<IActionResult> GetBalance(string accountId, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "GET /accounts/{accountId}/balance" } });
        var balance = await mediator.Send(new GetBalanceQuery(accountId), cancellationToken);
        return Ok(new { accountId, balance });
    }

    [HttpGet("{accountId}")]
    public async Task<IActionResult> GetAccount(string accountId, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "GET /accounts/{accountId}" } });
        var account = await mediator.Send(new GetAccountQuery(accountId), cancellationToken);
        return Ok(account);
    }
}

public sealed record ApplyTransactionRequest(Guid EventId, string EventType, decimal Amount, DateTimeOffset EventTimestamp);
