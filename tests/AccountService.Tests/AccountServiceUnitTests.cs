using AccountService.Application.Abstractions;
using AccountService.Application.Commands;
using AccountService.Application.Queries;
using AccountService.Domain.Entities;
using AccountService.Domain.Enums;
using FluentAssertions;

namespace AccountService.Tests;

public sealed class AccountServiceUnitTests
{
    [Fact]
    public async Task ApplyTransaction_WhenDuplicateEventId_ReturnsTrueAndSkipsPersist()
    {
        var duplicateEventId = Guid.NewGuid();
        var repository = new FakeAccountRepository
        {
            ExistingByEventId = new AccountTransaction
            {
                EventId = duplicateEventId,
                AccountId = "acct-dup",
                EventType = EventType.Credit,
                Amount = 10m,
                EventTimestamp = DateTimeOffset.UtcNow
            }
        };

        var handler = new ApplyTransactionCommandHandler(repository);

        var result = await handler.Handle(new ApplyTransactionCommand("acct-dup", duplicateEventId, "CREDIT", 10m, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeTrue();
        repository.AddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyTransaction_WhenNewEvent_PersistsAndReturnsFalse()
    {
        var repository = new FakeAccountRepository();
        var handler = new ApplyTransactionCommandHandler(repository);

        var eventId = Guid.NewGuid();
        var result = await handler.Handle(new ApplyTransactionCommand("acct-1", eventId, "DEBIT", 12m, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeFalse();
        repository.AddCallCount.Should().Be(1);
        repository.LastAdded.Should().NotBeNull();
        repository.LastAdded!.EventId.Should().Be(eventId);
        repository.LastAdded.EventType.Should().Be(EventType.Debit);
    }

    [Fact]
    public async Task GetAccount_WithOutOfOrderTransactions_ReturnsMappedTransactionsAndBalance()
    {
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-10);

        var repository = new FakeAccountRepository
        {
            ByAccount =
            [
                new AccountTransaction { EventId = Guid.NewGuid(), AccountId = "acct-order", EventType = EventType.Debit, Amount = 30m, EventTimestamp = later },
                new AccountTransaction { EventId = Guid.NewGuid(), AccountId = "acct-order", EventType = EventType.Credit, Amount = 100m, EventTimestamp = earlier }
            ],
            Balance = 70m
        };

        var handler = new GetAccountQueryHandler(repository);

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
        var repository = new FakeAccountRepository { Balance = 42m };
        var handler = new GetBalanceQueryHandler(repository);

        var result = await handler.Handle(new GetBalanceQuery("acct-balance"), CancellationToken.None);

        result.Should().Be(42m);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public AccountTransaction? ExistingByEventId { get; set; }
        public IReadOnlyList<AccountTransaction> ByAccount { get; set; } = [];
        public decimal Balance { get; set; }
        public int AddCallCount { get; private set; }
        public AccountTransaction? LastAdded { get; private set; }

        public Task<AccountTransaction?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingByEventId is not null && ExistingByEventId.EventId == eventId ? ExistingByEventId : null);

        public Task AddAsync(AccountTransaction accountTransaction, CancellationToken cancellationToken)
        {
            AddCallCount++;
            LastAdded = accountTransaction;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AccountTransaction>> GetByAccountAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(ByAccount);

        public Task<decimal> GetBalanceAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(Balance);
    }
}
