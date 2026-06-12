using EventGateway.Application.Abstractions;
using EventGateway.Infrastructure.Clients;
using EventGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Timeout;
using Polly.Extensions.Http;

namespace EventGateway.Infrastructure.DependencyInjection;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EventGatewayDbContext>(options => options.UseInMemoryDatabase("event-gateway-db"));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IAccountClient, AccountServiceClient>();

        var accountServiceBaseUrl = configuration["AccountService:BaseUrl"] ?? "http://localhost:8081";

        services.AddHttpClient("AccountServiceClient", client =>
            {
                client.BaseAddress = new Uri(accountServiceBaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800)]))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30)))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(2)));

        return services;
    }
}
