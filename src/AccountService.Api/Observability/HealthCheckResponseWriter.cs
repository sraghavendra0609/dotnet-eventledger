using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AccountService.Api.Observability;

public static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report, string serviceName)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsJsonAsync(new
        {
            service = serviceName,
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    data = entry.Value.Data
                })
        });
    }
}
