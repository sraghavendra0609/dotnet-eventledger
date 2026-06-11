using System.Diagnostics;
using EventGateway.Api.Observability;
using EventGateway.Application.Behaviors;
using EventGateway.Application.Exceptions;
using EventGateway.Application.Services;
using EventGateway.Infrastructure.DependencyInjection;
using EventGateway.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("service", "event-gateway")
        .WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddControllers();
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(EventGateway.Application.Commands.CreateEventCommand).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(EventGateway.Application.Commands.CreateEventCommand).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddSingleton<EventIdempotencyLock>();
builder.Services.AddGatewayInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<EventGatewayDatabaseHealthCheck>("database");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("event-gateway"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("EventGateway.Api")
        .AddConsoleExporter());

var app = builder.Build();

app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    using (LogContext.PushProperty("traceId", traceId))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionFeature?.Error;

        if (exception is ValidationException validationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                errors = validationException.Errors.Select(x => new { field = x.PropertyName, message = x.ErrorMessage })
            });
            return;
        }

        if (exception is AccountServiceUnavailableException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Account service is unavailable" });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Unexpected error" });
    });
});

app.MapControllers();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = (context, report) => HealthCheckResponseWriter.WriteAsync(context, report, "event-gateway")
});

app.Run();

public partial class Program;
