using EventGateway.Application.Abstractions;
using EventGateway.Application.Dto;
using MediatR;

namespace EventGateway.Application.Queries;

public sealed record GetEventsByAccountQuery(string AccountId) : IRequest<IReadOnlyList<EventDto>>;

public sealed class GetEventsByAccountQueryHandler(IEventRepository eventRepository) : IRequestHandler<GetEventsByAccountQuery, IReadOnlyList<EventDto>>
{
    public async Task<IReadOnlyList<EventDto>> Handle(GetEventsByAccountQuery request, CancellationToken cancellationToken)
    {
        var events = await eventRepository.GetByAccountAsync(request.AccountId, cancellationToken);
        return events.Select(EventDto.FromEntity).ToList();
    }
}
