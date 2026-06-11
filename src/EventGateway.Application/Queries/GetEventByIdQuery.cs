using EventGateway.Application.Abstractions;
using EventGateway.Application.Dto;
using MediatR;

namespace EventGateway.Application.Queries;

public sealed record GetEventByIdQuery(Guid Id) : IRequest<EventDto?>;

public sealed class GetEventByIdQueryHandler(IEventRepository eventRepository) : IRequestHandler<GetEventByIdQuery, EventDto?>
{
    public async Task<EventDto?> Handle(GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        var eventRecord = await eventRepository.GetByIdAsync(request.Id, cancellationToken);
        return eventRecord is null ? null : EventDto.FromEntity(eventRecord);
    }
}
