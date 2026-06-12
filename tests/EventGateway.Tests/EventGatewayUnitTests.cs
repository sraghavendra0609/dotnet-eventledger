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
using Moq;
using Moq.Protected;

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

        var repository = new Mock<IEventRepository>();
        repository
            .Setup(x => x.GetByEventIdAsync(existing.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var accountClient = new Mock<IAccountClient>();
        var handler = new CreateEventCommandHandler(repository.Object, accountClient.Object);

        var result = await handler.Handle(new CreateEventCommand(existing.EventId, "acct-dup", "CREDIT", 25m, "USD", existing.EventTimestamp), CancellationToken.None);

        result.IsDuplicate.Should().BeTrue();
        result.Event.EventId.Should().Be(existing.EventId);
        repository.Verify(x => x.AddAsync(It.IsAny<EventRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        accountClient.Verify(x => x.ApplyTransactionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<EventType>(), It.IsAny<decimal>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateEvent_WhenNewEvent_PersistsAndCallsDownstream()
    {
        EventRecord? addedRecord = null;
        var repository = new Mock<IEventRepository>();
        repository
            .Setup(x => x.GetByEventIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventRecord?)null);
        repository
            .Setup(x => x.AddAsync(It.IsAny<EventRecord>(), It.IsAny<CancellationToken>()))
            .Callback<EventRecord, CancellationToken>((record, _) => addedRecord = record)
            .Returns(Task.CompletedTask);
        var accountClient = new Mock<IAccountClient>();
        var handler = new CreateEventCommandHandler(repository.Object, accountClient.Object);

        var eventId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new CreateEventCommand(eventId, "acct-1", "DEBIT", 11m, "USD", timestamp), CancellationToken.None);

        result.IsDuplicate.Should().BeFalse();
        result.Event.EventId.Should().Be(eventId);
        result.Event.AccountId.Should().Be("acct-1");
        result.Event.EventType.Should().Be("DEBIT");
        addedRecord.Should().NotBeNull();
        addedRecord!.EventId.Should().Be(eventId);
        repository.Verify(x => x.AddAsync(It.IsAny<EventRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        accountClient.Verify(x => x.ApplyTransactionAsync("acct-1", eventId, EventType.Debit, 11m, timestamp, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEventsByAccount_WithOutOfOrderEvents_ReturnsRepositoryOrder()
    {
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-15);

        var events = (IReadOnlyList<EventRecord>)
        [
            new EventRecord { EventId = Guid.NewGuid(), AccountId = "acct-sort", EventType = EventType.Debit, Amount = 20m, EventTimestamp = earlier },
            new EventRecord { EventId = Guid.NewGuid(), AccountId = "acct-sort", EventType = EventType.Credit, Amount = 30m, EventTimestamp = later }
        ];

        var repository = new Mock<IEventRepository>();
        repository
            .Setup(x => x.GetByAccountAsync("acct-sort", It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        var handler = new GetEventsByAccountQueryHandler(repository.Object);

        var result = await handler.Handle(new GetEventsByAccountQuery("acct-sort"), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].EventTimestamp.Should().Be(earlier);
        result[1].EventTimestamp.Should().Be(later);
    }

    [Fact]
    public async Task GetEventById_WhenFound_ReturnsMappedEvent()
    {
        var id = Guid.NewGuid();
        var repository = new Mock<IEventRepository>();
        repository
            .Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventRecord
            {
                Id = id,
                EventId = id,
                AccountId = "acct-1",
                EventType = EventType.Credit,
                Amount = 5m,
                EventTimestamp = DateTimeOffset.UtcNow
            });

        var handler = new GetEventByIdQueryHandler(repository.Object);
        var result = await handler.Handle(new GetEventByIdQuery(id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetEventById_WhenMissing_ReturnsNull()
    {
        var repository = new Mock<IEventRepository>();
        repository
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventRecord?)null);

        var handler = new GetEventByIdQueryHandler(repository.Object);
        var result = await handler.Handle(new GetEventByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AccountServiceClient_WhenDownstreamReturnsFailure_ThrowsUnavailableException()
    {
        var httpMessageHandler = new Mock<HttpMessageHandler>();
        httpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var httpClient = new HttpClient(httpMessageHandler.Object) { BaseAddress = new Uri("http://account-service") };
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient("AccountServiceClient")).Returns(httpClient);
        var sut = new AccountServiceClient(httpClientFactory.Object);

        var act = () => sut.ApplyTransactionAsync("acct-1", Guid.NewGuid(), EventType.Credit, 5m, DateTimeOffset.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<AccountServiceUnavailableException>();
    }

    [Fact]
    public async Task AccountServiceClient_AddsTraceParentHeader_WhenActivityExists()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpMessageHandler = new Mock<HttpMessageHandler>();
        httpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Accepted));

        var httpClient = new HttpClient(httpMessageHandler.Object) { BaseAddress = new Uri("http://account-service") };
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient("AccountServiceClient")).Returns(httpClient);
        var sut = new AccountServiceClient(httpClientFactory.Object);

        using var activity = new Activity("unit-test").Start();
        await sut.ApplyTransactionAsync("acct-1", Guid.NewGuid(), EventType.Credit, 9m, DateTimeOffset.UtcNow, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains("traceparent").Should().BeTrue();
    }
}
