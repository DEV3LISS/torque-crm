using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Torque.Application.Common;
using Torque.Domain.Messaging;
using Torque.Infrastructure.Persistence;

namespace Torque.Api.Setup;

/// <summary>
/// Three pillars: <b>logs</b> (Serilog, see Program.cs), <b>metrics</b> (Prometheus scrape at /metrics)
/// and <b>traces</b> (OTLP → Jaeger/Tempo/any vendor, only if an endpoint is configured).
/// </summary>
public static class Observability
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("torque-crm"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()   // http.server.request.duration by route and status → RED metrics
                .AddRuntimeInstrumentation()      // GC, thread pool, exceptions
                .AddMeter(TorqueMetrics.MeterName)
                .AddMeter("Npgsql")               // connection pool usage, command duration
                .AddPrometheusExporter())
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")
                                                                         && !ctx.Request.Path.StartsWithSegments("/metrics"))
                    .AddSource("Npgsql"); // every SQL command as a child span: see which query made a request slow
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }
}

/// <summary>
/// The outbox is "degraded" when reminders are stuck: messages that failed for good, or due messages
/// waiting more than 15 minutes (the worker is down or the gateway keeps failing). The API itself still
/// works — so this is Degraded, not Unhealthy, and it is not part of readiness.
/// </summary>
public sealed class OutboxHealthCheck(AppDbContext db, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = clock.GetUtcNow().AddMinutes(-15);
        var stuck = await db.Set<OutboxMessage>().CountAsync(m => m.Status == OutboxStatus.Pending && m.DueAt < threshold, cancellationToken);
        var failed = await db.Set<OutboxMessage>().CountAsync(m => m.Status == OutboxStatus.Failed, cancellationToken);

        var data = new Dictionary<string, object> { ["overdue"] = stuck, ["failed"] = failed };
        return stuck > 0 || failed > 0
            ? HealthCheckResult.Degraded($"{stuck} overdue and {failed} failed outbox messages", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
