using System.Diagnostics;
using System.Net.Http.Json;
using EventGateway.Application.Abstractions;
using EventGateway.Application.Exceptions;
using EventGateway.Domain.Enums;

namespace EventGateway.Infrastructure.Clients;

public sealed class AccountServiceClient(IHttpClientFactory httpClientFactory) : IAccountClient
{
    public async Task ApplyTransactionAsync(string accountId, Guid eventId, EventType eventType, decimal amount, DateTimeOffset eventTimestamp, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AccountServiceClient");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{accountId}/transactions")
        {
            Content = JsonContent.Create(new
            {
                eventId,
                eventType = eventType.ToString().ToUpperInvariant(),
                amount,
                eventTimestamp
            })
        };

        AddTraceParent(request);

        var response = await client.SendAsync(request, cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AccountServiceUnavailableException("Account service request failed.");
            }
        }
    }

    public async Task<decimal> GetBalanceAsync(string accountId, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AccountServiceClient");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{accountId}/balance");
        AddTraceParent(request);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AccountServiceUnavailableException("Account service is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AccountServiceUnavailableException("Account service request failed.");
            }

            var result = await response.Content.ReadFromJsonAsync<BalanceResult>(cancellationToken: cancellationToken);
            return result?.Balance ?? 0m;
        }
    }

    private static void AddTraceParent(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Activity.Current?.Id))
        {
            request.Headers.TryAddWithoutValidation("traceparent", Activity.Current.Id);
        }
    }

    private sealed record BalanceResult(string AccountId, decimal Balance);
}
