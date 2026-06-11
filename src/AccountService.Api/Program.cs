using System.Diagnostics;
using AccountService.Api.Observability;
using AccountService.Application.Behaviors;
using AccountService.Application.Services;
using AccountService.Infrastructure.DependencyInjection;
using AccountService.Infrastructure.Persistence;
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
        .Enrich.WithProperty("service", "account-service")
        .WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddControllers();
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(AccountService.Application.Commands.ApplyTransactionCommand).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(AccountService.Application.Commands.ApplyTransactionCommand).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddSingleton<TransactionIdempotencyLock>();
builder.Services.AddAccountInfrastructure();
builder.Services.AddHealthChecks()
    .AddCheck<AccountServiceDatabaseHealthCheck>("database");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("account-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("AccountService.Api")
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
        if (exceptionFeature?.Error is ValidationException validationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                errors = validationException.Errors.Select(x => new { field = x.PropertyName, message = x.ErrorMessage })
            });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Unexpected error" });
    });
});

app.MapControllers();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = (context, report) => HealthCheckResponseWriter.WriteAsync(context, report, "account-service")
});

app.Run();

public partial class Program;
