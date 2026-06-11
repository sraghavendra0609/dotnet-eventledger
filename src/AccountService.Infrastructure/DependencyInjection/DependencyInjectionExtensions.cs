using AccountService.Application.Abstractions;
using AccountService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccountService.Infrastructure.DependencyInjection;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddAccountInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AccountDbContext>(options => options.UseInMemoryDatabase("account-service-db"));
        services.AddScoped<IAccountRepository, AccountRepository>();
        return services;
    }
}
