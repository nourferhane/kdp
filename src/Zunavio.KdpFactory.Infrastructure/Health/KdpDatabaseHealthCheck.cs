using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Zunavio.KdpFactory.Infrastructure.Health;

/// <summary>
/// Readiness probe for PostgreSQL: opens a pooled connection and runs SELECT 1.
/// Registered on /health/ready so orchestrators (Koyeb, Render, etc.) only mark
/// the app ready once the database is actually reachable.
/// </summary>
public sealed class KdpDatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL unreachable.", ex);
        }
    }
}