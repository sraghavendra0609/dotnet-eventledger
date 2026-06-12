using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EventGateway.Application.Abstractions;
using EventGateway.Application.Commands;
using EventGateway.Application.Exceptions;
using EventGateway.Application.Queries;
using EventGateway.Domain.Entities;
using EventGateway.Domain.Enums;
using EventGateway.Infrastructure.Clients;
using FluentAssertions;

namespace EventGateway.Tests;

public sealed class EventGatewayUnitTests
{
    [Fact]
    public async Task CreateEvent_WhenDuplicateEventId_ReturnsDuplicateAndSkipsDownstreamAndPersist()
    {
        var existing = new EventRecord
        {
            EventId = Guid.NewGuid(),
            AccountId = "acct-dup",
            EventType = EventType.Credit,
            Amount = 25m,
            EventTimestamp = DateTimeOffset.UtcNow
        };

        var repository = new FakeEventRepository { ExistingByEventId = existing };
        var accountClient = new FakeAccountClient();
        var handler = new CreateEventCommandHandler(repository, accountClient);

        var result = await handler.Handle(new CreateEventCommand(existing.EventId, "acct-dup", "CREDIT", 25m, existing.EventTimestamp), CancellationToken.None);

        result.IsDuplicate.Should().BeTrue();
        result.Event.EventId.Should().Be(existing.EventId);
        accountClient.CallCount.Should().Be(0);
        repository.AddCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateEvent_WhenNewEvent_PersistsAndCallsDownstream()
    {
        var repository = new FakeEventRepository();
        var accountClient = new FakeAccountClient();
        var handler = new CreateEventCommandHandler(repository, accountClient);

        var eventId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new CreateEventCommand(eventId, "acct-1", "DEBIT", 11m, timestamp), CancellationToken.None);

        result.IsDuplicate.Should().BeFalse();
        result.Event.EventId.Should().Be(eventId);
        result.Event.AccountId.Should().Be("acct-1");
        result.Event.EventType.Should().Be("DEBIT");
        accountClient.CallCount.Should().Be(1);
        repository.AddCallCount.Should().Be(1);
        repository.LastAdded.Should().NotBeNull();
        repository.LastAdded!.EventId.Should().Be(eventId);
    }

    [Fact]
    public async Task GetEventsByAccount_WithOutOfOrderEvents_ReturnsRepositoryOrder()
    {
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-15);

        var repository = new FakeEventRepository
        {
            EventsByAccount =
            [
                new EventRecord { EventId = Guid.NewGuid(), AccountId = "acct-sort", EventType = EventType.Debit, Amount = 20m, EventTimestamp = earlier },
                new EventRecord { EventId = Guid.NewGuid(), AccountId = "acct-sort", EventType = EventType.Credit, Amount = 30m, EventTimestamp = later }
            ]
        };

        var handler = new GetEventsByAccountQueryHandler(repository);

        var result = await handler.Handle(new GetEventsByAccountQuery("acct-sort"), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].EventTimestamp.Should().Be(earlier);
        result[1].EventTimestamp.Should().Be(later);
    }

    [Fact]
    public async Task AccountServiceClient_WhenDownstreamReturnsFailure_ThrowsUnavailableException()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://account-service") };
        var sut = new AccountServiceClient(new FakeHttpClientFactory(httpClient));

        var act = () => sut.ApplyTransactionAsync("acct-1", Guid.NewGuid(), EventType.Credit, 5m, DateTimeOffset.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<AccountServiceUnavailableException>();
    }

    [Fact]
    public async Task AccountServiceClient_AddsTraceParentHeader_WhenActivityExists()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://account-service") };
        var sut = new AccountServiceClient(new FakeHttpClientFactory(httpClient));

        using var activity = new Activity("unit-test").Start();
        await sut.ApplyTransactionAsync("acct-1", Guid.NewGuid(), EventType.Credit, 9m, DateTimeOffset.UtcNow, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Headers.Contains("traceparent").Should().BeTrue();
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public EventRecord? ExistingByEventId { get; set; }
        public IReadOnlyList<EventRecord> EventsByAccount { get; set; } = [];
        public int AddCallCount { get; private set; }
        public EventRecord? LastAdded { get; private set; }

        public Task<EventRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingByEventId is not null && ExistingByEventId.EventId == eventId ? ExistingByEventId : null);

        public Task<EventRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<EventRecord?>(null);

        public Task<IReadOnlyList<EventRecord>> GetByAccountAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(EventsByAccount);

        public Task AddAsync(EventRecord eventRecord, CancellationToken cancellationToken)
        {
            AddCallCount++;
            LastAdded = eventRecord;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountClient : IAccountClient
    {
        public int CallCount { get; private set; }

        public Task ApplyTransactionAsync(string accountId, Guid eventId, EventType eventType, decimal amount, DateTimeOffset eventTimestamp, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
