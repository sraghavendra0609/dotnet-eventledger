using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EventGateway.Application.Abstractions;
using EventGateway.Application.Exceptions;
using EventGateway.Infrastructure.Clients;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventGateway.Tests;

public sealed class EventGatewayApiTests
{
    [Fact]
    public async Task PostingSameEventTwice_IsIdempotent()
    {
        var fake = new CountingAccountClient();
        await using var factory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddSingleton<IAccountClient>(fake);
        });

        var client = factory.CreateClient();
        var eventId = Guid.NewGuid();

        await client.PostAsJsonAsync("/events", new
        {
            eventId,
            accountId = "acct-1",
            type = "CREDIT",
            amount = 12m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });

        var duplicate = await client.PostAsJsonAsync("/events", new
        {
            eventId,
            accountId = "acct-1",
            type = "CREDIT",
            amount = 12m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });

        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task EventsAreReturnedSortedByTimestamp()
    {
        var fake = new CountingAccountClient();
        await using var factory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddSingleton<IAccountClient>(fake);
        });
        var client = factory.CreateClient();

        var accountId = "acct-sort";
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-10);

        await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId,
            type = "CREDIT",
            amount = 10m,
            currency = "USD",
            eventTimestamp = later
        });

        await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId,
            type = "DEBIT",
            amount = 5m,
            currency = "USD",
            eventTimestamp = earlier
        });

        var result = await client.GetFromJsonAsync<List<EventResponse>>($"/events?account={accountId}");
        result.Should().NotBeNull();
        result![0].EventTimestamp.Should().Be(earlier);
        result[1].EventTimestamp.Should().Be(later);
    }

    [Fact]
    public async Task WhenAccountServiceIsDown_PostReturns503_AndGetStillWorks()
    {
        var toggle = new ToggleAccountClient();
        await using var factory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddSingleton<IAccountClient>(toggle);
        });
        var client = factory.CreateClient();

        var firstId = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/events", new
        {
            eventId = firstId,
            accountId = "acct-res",
            type = "CREDIT",
            amount = 50m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        toggle.Fail = true;

        var failed = await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-res",
            type = "DEBIT",
            amount = 2m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });
        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var query = await client.GetAsync("/events?account=acct-res");
        query.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TraceParentHeader_IsPropagatedToAccountService()
    {
        string? traceParent = null;
        var handler = new DelegatingHandlerStub(request =>
        {
            traceParent = request.Headers.TryGetValues("traceparent", out var values) ? values.SingleOrDefault() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        });

        await using var factory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddScoped<IAccountClient, AccountServiceClient>();
            services.AddHttpClient("AccountServiceClient", client => client.BaseAddress = new Uri("http://account-service"))
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        });

        var client = factory.CreateClient();

        using var activity = new Activity("test").Start();
        await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-trace",
            type = "CREDIT",
            amount = 100m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });

        traceParent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FullGatewayToAccountFlow_UpdatesAccountBalance()
    {
        await using var accountFactory = new WebApplicationFactory<AccountService.Api.ApiMarker>();
        var accountClient = accountFactory.CreateClient();

        var forwardingHandler = new ForwardToHttpClientHandler(accountClient);
        await using var gatewayFactory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddScoped<IAccountClient, AccountServiceClient>();
            services.AddHttpClient("AccountServiceClient", client => client.BaseAddress = accountClient.BaseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => forwardingHandler);
        });

        var gatewayClient = gatewayFactory.CreateClient();

        var eventResponse = await gatewayClient.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-flow",
            type = "CREDIT",
            amount = 42m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });
        eventResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await accountClient.GetFromJsonAsync<BalanceResponse>("/accounts/acct-flow/balance");
        balance!.Balance.Should().Be(42m);
    }

    [Fact]
    public async Task InvalidPayload_ReturnsBadRequest()
    {
        await using var factory = CreateGatewayFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-4",
            type = "UNKNOWN",
            amount = 20m,
            currency = "USD",
            eventTimestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingCurrency_ReturnsBadRequest()
    {
        await using var factory = CreateGatewayFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-5",
            type = "CREDIT",
            amount = 10m,
            currency = string.Empty,
            eventTimestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingEventTimestamp_ReturnsBadRequest()
    {
        await using var factory = CreateGatewayFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            accountId = "acct-6",
            type = "CREDIT",
            amount = 10m,
            currency = "USD",
            eventTimestamp = default(DateTimeOffset)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MetadataIsStoredAndReturnedWithEvent()
    {
        var fake = new CountingAccountClient();
        await using var factory = CreateGatewayFactory(services =>
        {
            services.RemoveAll<IAccountClient>();
            services.AddSingleton<IAccountClient>(fake);
        });

        var client = factory.CreateClient();
        var eventId = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync("/events", new
        {
            eventId,
            accountId = "acct-meta",
            type = "CREDIT",
            amount = 75m,
            currency = "EUR",
            eventTimestamp = DateTimeOffset.UtcNow,
            metadata = new Dictionary<string, string> { ["source"] = "mainframe-batch", ["batchId"] = "B-9042" }
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<EventDetailsResponse>();
        created!.Currency.Should().Be("EUR");
        created.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("mainframe-batch");
        created.Metadata.Should().ContainKey("batchId").WhoseValue.Should().Be("B-9042");
    }

    private static WebApplicationFactory<EventGateway.Api.ApiMarker> CreateGatewayFactory(Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<EventGateway.Api.ApiMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    configureServices?.Invoke(services);
                });
            });
    }

    private sealed class CountingAccountClient : IAccountClient
    {
        public int CallCount { get; private set; }

        public Task ApplyTransactionAsync(string accountId, Guid eventId, EventGateway.Domain.Enums.EventType eventType, decimal amount, DateTimeOffset eventTimestamp, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ToggleAccountClient : IAccountClient
    {
        public bool Fail { get; set; }

        public Task ApplyTransactionAsync(string accountId, Guid eventId, EventGateway.Domain.Enums.EventType eventType, decimal amount, DateTimeOffset eventTimestamp, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new AccountServiceUnavailableException("Account service down");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class ForwardToHttpClientHandler(HttpClient targetClient) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var forward = new HttpRequestMessage(request.Method, request.RequestUri!.PathAndQuery)
            {
                Content = request.Content is null ? null : new StreamContent(await request.Content.ReadAsStreamAsync(cancellationToken))
            };

            foreach (var header in request.Headers)
            {
                forward.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    forward.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return await targetClient.SendAsync(forward, cancellationToken);
        }
    }

    private sealed record EventResponse(DateTimeOffset EventTimestamp);
    private sealed record EventDetailsResponse(string Currency, Dictionary<string, string>? Metadata);
    private sealed record BalanceResponse(string AccountId, decimal Balance);
}
