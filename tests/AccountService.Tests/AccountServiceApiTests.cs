using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AccountService.Tests;

public sealed class AccountServiceApiTests : IClassFixture<WebApplicationFactory<AccountService.Api.ApiMarker>>
{
    private readonly HttpClient _client;

    public AccountServiceApiTests(WebApplicationFactory<AccountService.Api.ApiMarker> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Balance_IsComputedAsCreditsMinusDebits_RegardlessOfOrder()
    {
        var accountId = "acct-1";

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId = Guid.NewGuid(),
            eventType = "DEBIT",
            amount = 30m,
            eventTimestamp = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId = Guid.NewGuid(),
            eventType = "CREDIT",
            amount = 100m,
            eventTimestamp = DateTimeOffset.UtcNow
        });

        var response = await _client.GetFromJsonAsync<BalanceResponse>($"/accounts/{accountId}/balance");
        response.Should().NotBeNull();
        response!.Balance.Should().Be(70m);
    }

    [Fact]
    public async Task PostingSameEventTwice_IsIdempotent()
    {
        var accountId = "acct-2";
        var eventId = Guid.NewGuid();

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId,
            eventType = "CREDIT",
            amount = 50m,
            eventTimestamp = DateTimeOffset.UtcNow
        });

        var duplicateResponse = await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId,
            eventType = "CREDIT",
            amount = 50m,
            eventTimestamp = DateTimeOffset.UtcNow
        });

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var balance = await _client.GetFromJsonAsync<BalanceResponse>($"/accounts/{accountId}/balance");
        balance!.Balance.Should().Be(50m);
    }

    [Fact]
    public async Task InvalidPayload_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync($"/accounts/acct-3/transactions", new
        {
            eventId = Guid.NewGuid(),
            eventType = "CREDIT",
            amount = 0m,
            eventTimestamp = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record BalanceResponse(string AccountId, decimal Balance);
}
