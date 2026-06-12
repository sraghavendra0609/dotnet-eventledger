using AccountService.Application.Abstractions;
using AccountService.Application.Commands;
using AccountService.Application.Queries;
using AccountService.Application.Services;
using AccountService.Domain.Entities;
using AccountService.Domain.Enums;
using FluentAssertions;
using Moq;

namespace AccountService.Tests;

public sealed class AccountServiceUnitTests
{
    [Fact]
    public async Task ApplyTransaction_WhenDuplicateEventId_ReturnsTrueAndSkipsPersist()
    {
        var duplicateEventId = Guid.NewGuid();
        var repository = new Mock<IAccountRepository>();
        repository
            .Setup(x => x.GetByEventIdAsync(duplicateEventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountTransaction
            {
                EventId = duplicateEventId,
                AccountId = "acct-dup",
                EventType = EventType.Credit,
                Amount = 10m,
                EventTimestamp = DateTimeOffset.UtcNow
            });

        var handler = new ApplyTransactionCommandHandler(repository.Object, new TransactionIdempotencyLock());

        var result = await handler.Handle(new ApplyTransactionCommand("acct-dup", duplicateEventId, "CREDIT", 10m, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeTrue();
        repository.Verify(x => x.AddAsync(It.IsAny<AccountTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyTransaction_WhenNewEvent_PersistsAndReturnsFalse()
    {
        AccountTransaction? addedTransaction = null;
        var repository = new Mock<IAccountRepository>();
        repository
            .Setup(x => x.GetByEventIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountTransaction?)null);
        repository
            .Setup(x => x.AddAsync(It.IsAny<AccountTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<AccountTransaction, CancellationToken>((transaction, _) => addedTransaction = transaction)
            .Returns(Task.CompletedTask);
        var handler = new ApplyTransactionCommandHandler(repository.Object, new TransactionIdempotencyLock());

        var eventId = Guid.NewGuid();
        var result = await handler.Handle(new ApplyTransactionCommand("acct-1", eventId, "DEBIT", 12m, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeFalse();
        addedTransaction.Should().NotBeNull();
        addedTransaction!.EventId.Should().Be(eventId);
        addedTransaction.EventType.Should().Be(EventType.Debit);
        repository.Verify(x => x.AddAsync(It.IsAny<AccountTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAccount_WithOutOfOrderTransactions_ReturnsMappedTransactionsAndBalance()
    {
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-10);

        var repository = new Mock<IAccountRepository>();
        repository
            .Setup(x => x.GetByAccountAsync("acct-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IReadOnlyList<AccountTransaction>)
                [
                    new AccountTransaction { EventId = Guid.NewGuid(), AccountId = "acct-order", EventType = EventType.Debit, Amount = 30m, EventTimestamp = later },
                    new AccountTransaction { EventId = Guid.NewGuid(), AccountId = "acct-order", EventType = EventType.Credit, Amount = 100m, EventTimestamp = earlier }
                ]);
        repository
            .Setup(x => x.GetBalanceAsync("acct-order", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70m);

        var handler = new GetAccountQueryHandler(repository.Object);

        var result = await handler.Handle(new GetAccountQuery("acct-order"), CancellationToken.None);

        result.AccountId.Should().Be("acct-order");
        result.Balance.Should().Be(70m);
        result.Transactions.Should().HaveCount(2);
        result.Transactions[0].EventTimestamp.Should().Be(later);
        result.Transactions[1].EventTimestamp.Should().Be(earlier);
    }

    [Fact]
    public async Task GetBalance_ForwardsRepositoryBalance()
    {
        var repository = new Mock<IAccountRepository>();
        repository
            .Setup(x => x.GetBalanceAsync("acct-balance", It.IsAny<CancellationToken>()))
            .ReturnsAsync(42m);
        var handler = new GetBalanceQueryHandler(repository.Object);

        var result = await handler.Handle(new GetBalanceQuery("acct-balance"), CancellationToken.None);

        result.Should().Be(42m);
    }
}
