using AccountService.Application.Abstractions;
using AccountService.Application.Services;
using AccountService.Domain.Entities;
using AccountService.Domain.Enums;
using FluentValidation;
using MediatR;

namespace AccountService.Application.Commands;

public sealed record ApplyTransactionCommand(string AccountId, Guid EventId, string EventType, decimal Amount, DateTimeOffset EventTimestamp) : IRequest<bool>;

public sealed class ApplyTransactionCommandValidator : AbstractValidator<ApplyTransactionCommand>
{
    public ApplyTransactionCommandValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.EventType).NotEmpty().Must(x => Enum.TryParse<EventType>(x, true, out _)).WithMessage("eventType must be CREDIT or DEBIT");
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class ApplyTransactionCommandHandler(IAccountRepository accountRepository, TransactionIdempotencyLock idempotencyLock) : IRequestHandler<ApplyTransactionCommand, bool>
{
    public async Task<bool> Handle(ApplyTransactionCommand request, CancellationToken cancellationToken)
    {
        var semaphore = idempotencyLock.GetLockFor(request.EventId);
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var existing = await accountRepository.GetByEventIdAsync(request.EventId, cancellationToken);
            if (existing is not null)
            {
                return true;
            }

            var transaction = new AccountTransaction
            {
                AccountId = request.AccountId,
                EventId = request.EventId,
                EventType = Enum.Parse<EventType>(request.EventType, true),
                Amount = request.Amount,
                EventTimestamp = request.EventTimestamp
            };

            await accountRepository.AddAsync(transaction, cancellationToken);
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
