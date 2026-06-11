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

        if (!string.IsNullOrWhiteSpace(Activity.Current?.Id))
        {
            request.Headers.TryAddWithoutValidation("traceparent", Activity.Current.Id);
        }

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountServiceUnavailableException("Account service request failed.");
        }
    }
}
