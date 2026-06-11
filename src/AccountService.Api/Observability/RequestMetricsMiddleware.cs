using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Routing;

namespace AccountService.Api.Observability;

public sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    private static readonly Meter Meter = new("AccountService.Api");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "http_request_duration_ms",
        unit: "ms",
        description: "HTTP request duration in milliseconds by endpoint");

    private static readonly Counter<long> RequestErrors = Meter.CreateCounter<long>(
        "http_request_errors_total",
        description: "Total HTTP requests that resulted in an error response (4xx or 5xx) by endpoint");

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            var routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var endpoint = routePattern is not null
                ? $"{method} /{routePattern.TrimStart('/')}"
                : $"{method} {context.Request.Path.Value ?? "unknown"}";

            var statusCode = context.Response.StatusCode;
            var tags = new TagList
            {
                { "endpoint", endpoint },
                { "status_code", statusCode }
            };

            RequestDuration.Record(sw.Elapsed.TotalMilliseconds, tags);

            if (statusCode >= 400)
            {
                RequestErrors.Add(1, tags);
            }
        }
    }
}
