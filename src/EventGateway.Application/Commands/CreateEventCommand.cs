using EventGateway.Application.Abstractions;
using EventGateway.Application.Dto;
using EventGateway.Domain.Entities;
using EventGateway.Domain.Enums;
using FluentValidation;
using MediatR;

namespace EventGateway.Application.Commands;

public sealed record CreateEventCommand(
    Guid EventId,
    string AccountId,
    string EventType,
    decimal Amount,
    string Currency,
    DateTimeOffset EventTimestamp,
    Dictionary<string, string>? Metadata = null) : IRequest<CreateEventResult>;

public sealed record CreateEventResult(EventDto Event, bool IsDuplicate);

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EventType).NotEmpty().Must(x => Enum.TryParse<EventType>(x, true, out _)).WithMessage("eventType must be CREDIT or DEBIT");
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty();
        RuleFor(x => x.EventTimestamp).NotEqual(default(DateTimeOffset)).WithMessage("eventTimestamp must be a valid date");
    }
}

public sealed class CreateEventCommandHandler(IEventRepository eventRepository, IAccountClient accountClient) : IRequestHandler<CreateEventCommand, CreateEventResult>
{
    public async Task<CreateEventResult> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var existing = await eventRepository.GetByEventIdAsync(request.EventId, cancellationToken);
        if (existing is not null)
        {
            return new CreateEventResult(EventDto.FromEntity(existing), true);
        }

        var parsedType = Enum.Parse<EventType>(request.EventType, true);
        await accountClient.ApplyTransactionAsync(request.AccountId, request.EventId, parsedType, request.Amount, request.EventTimestamp, cancellationToken);

        var eventRecord = new EventRecord
        {
            EventId = request.EventId,
            AccountId = request.AccountId,
            EventType = parsedType,
            Amount = request.Amount,
            Currency = request.Currency,
            EventTimestamp = request.EventTimestamp,
            Metadata = request.Metadata
        };

        await eventRepository.AddAsync(eventRecord, cancellationToken);
        return new CreateEventResult(EventDto.FromEntity(eventRecord), false);
    }
}
