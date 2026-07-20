using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NotesService.Data;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly NotesDbContext db;

    public DatabaseHealthCheck(NotesDbContext db)
    {
        this.db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("cannot connect to the database");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("database check failed", exception);
        }
    }
}
