using Microsoft.Extensions.Diagnostics.HealthChecks;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Api.Health;

public sealed class DatabaseConnectivityHealthCheck : IHealthCheck
{
    private readonly SifenDbContext _dbContext;

    public DatabaseConnectivityHealthCheck(SifenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("SQL Server is reachable.")
                : HealthCheckResult.Degraded("SQL Server is configured but not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("SQL Server connectivity check failed.", ex);
        }
    }
}
