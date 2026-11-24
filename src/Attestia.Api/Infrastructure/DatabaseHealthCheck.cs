using Attestia.Data.Repositories;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Attestia.Api.Infrastructure;

/// <summary>Verifica che il database risponda eseguendo una procedura leggera (il login dell'API ha solo EXECUTE).</summary>
internal sealed class DatabaseHealthCheck(IReportRepository reports) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await reports.ExpiringSoonDaysAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // un health check deve trasformare qualunque errore in "Unhealthy"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Database non raggiungibile.", ex);
        }
    }
}
