using MduAssets.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MduAssets;

/// <summary>Reports healthy when the database answers a query.</summary>
internal sealed class DatabaseHealthCheck(Database database) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.ExecuteScalar();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The database does not answer.", exception));
        }
    }
}
