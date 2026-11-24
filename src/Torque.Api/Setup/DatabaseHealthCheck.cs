using Microsoft.Extensions.Diagnostics.HealthChecks;
using Torque.Infrastructure.Persistence;

namespace Torque.Api.Setup;

/// <summary>Readiness: the app is only "ready" for traffic when it can reach PostgreSQL.</summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
}
