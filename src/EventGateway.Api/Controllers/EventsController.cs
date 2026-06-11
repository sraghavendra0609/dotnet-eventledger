using System.Diagnostics;
using System.Diagnostics.Metrics;
using EventGateway.Application.Commands;
using EventGateway.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EventGateway.Api.Controllers;

[ApiController]
[Route("events")]
public sealed class EventsController(IMediator mediator) : ControllerBase
{
    private static readonly Meter Meter = new("EventGateway.Api");
    private static readonly Counter<long> RequestsCounter = Meter.CreateCounter<long>("event_gateway_requests_total");

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "POST /events" } });
        var result = await mediator.Send(new CreateEventCommand(request.EventId, request.AccountId, request.EventType, request.Amount, request.EventTimestamp), cancellationToken);

        if (result.IsDuplicate)
        {
            return Ok(result.Event);
        }

        return CreatedAtAction(nameof(GetEventById), new { id = result.Event.Id }, result.Event);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetEventById(Guid id, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "GET /events/{id}" } });
        var result = await mediator.Send(new GetEventByIdQuery(id), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents([FromQuery(Name = "account")] string accountId, CancellationToken cancellationToken)
    {
        RequestsCounter.Add(1, new TagList { { "endpoint", "GET /events" } });
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return BadRequest(new { error = "account query parameter is required" });
        }

        var result = await mediator.Send(new GetEventsByAccountQuery(accountId), cancellationToken);
        return Ok(result);
    }
}

public sealed record CreateEventRequest(Guid EventId, string AccountId, string EventType, decimal Amount, DateTimeOffset EventTimestamp);
