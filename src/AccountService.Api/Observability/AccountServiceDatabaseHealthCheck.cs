using AccountService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AccountService.Api.Observability;

public sealed class AccountServiceDatabaseHealthCheck(AccountDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var isInMemory = dbContext.Database.IsInMemory();
        var canConnect = isInMemory || await dbContext.Database.CanConnectAsync(cancellationToken);
        var diagnostics = new Dictionary<string, object>
        {
            ["provider"] = dbContext.Database.ProviderName ?? "unknown",
            ["storage"] = isInMemory ? "in-memory" : "external",
            ["connectivity"] = canConnect ? "reachable" : "unreachable"
        };

        return canConnect
            ? HealthCheckResult.Healthy("Database connectivity is healthy.", diagnostics)
            : HealthCheckResult.Unhealthy("Database connectivity failed.", data: diagnostics);
    }
}
