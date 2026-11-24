using System.ComponentModel.DataAnnotations;
using Attestia.Api.Auth;
using Attestia.Core.Time;
using Attestia.Data.Repositories;

namespace Attestia.Api.Endpoints;

internal static class ReportEndpoints
{
    public static RouteGroupBuilder MapReports(this RouteGroupBuilder api)
    {
        api.MapGet("/expirations", async (IReportRepository repo, IBusinessClock clock, CancellationToken ct,
                    [Range(0, 3650)] int withinDays = 60,
                    [StringLength(100)] string? department = null,
                    [Range(1, 100_000)] int page = 1,
                    [Range(1, 200)] int pageSize = 20) =>
                TypedResults.Ok(await repo.ExpiringAsync(withinDays, department, page, pageSize, clock.Today, ct)))
            .WithTags("Reports")
            .RequireAuthorization(Policies.Hr)
            .WithName("listExpirations")
            .WithSummary("Attestati scaduti o in scadenza entro N giorni (paginazione lato server)");

        api.MapGet("/summary/departments", async (IReportRepository repo, IBusinessClock clock, CancellationToken ct) =>
                TypedResults.Ok(await repo.DepartmentSummaryAsync(clock.Today, ct)))
            .WithTags("Reports")
            .RequireAuthorization(Policies.Hr)
            .WithName("getDepartmentSummary")
            .WithSummary("Riepilogo per reparto: validi, in scadenza, scaduti, mancanti obbligatori");

        return api;
    }
}
