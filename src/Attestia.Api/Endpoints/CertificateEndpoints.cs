using Attestia.Api.Auth;
using Attestia.Core.Errors;
using Attestia.Core.Models;
using Attestia.Core.Time;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Attestia.Api.Endpoints;

internal static class CertificateEndpoints
{
    private static readonly DateOnly MinDate = new(2000, 1, 1);

    public static RouteGroupBuilder MapCertificates(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/certificates").WithTags("Attestati").RequireAuthorization(Policies.Hr);

        g.MapGet("/{id:int}", async Task<Results<Ok<CertificateRecord>, NotFound>> (int id, ICertificateRepository repo, IBusinessClock clock, CancellationToken ct) =>
                await repo.GetAsync(id, clock.Today, ct) is { } c ? TypedResults.Ok(c) : TypedResults.NotFound())
            .WithName("GetCertificate")
            .WithSummary("Dettaglio di un attestato");

        g.MapPost("/", async (CreateCertificateRequest req, ICertificateRepository repo, IBusinessClock clock, CancellationToken ct) =>
            {
                EnsureValidCompletionDate(req.CompletedOn, clock.Today);
                var created = await repo.InsertAsync(new CertificateInput(req.EmployeeNumber, req.CourseCode, req.CompletedOn, req.Score), clock.Today, ct);
                return TypedResults.CreatedAtRoute(created, "GetCertificate", new { id = created.Id });
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Registra manualmente un completamento");

        g.MapPut("/{id:int}", async Task<Results<Ok<CertificateRecord>, NotFound>> (int id, UpdateCertificateRequest req, ICertificateRepository repo,
                    IBusinessClock clock, CancellationToken ct) =>
            {
                EnsureValidCompletionDate(req.CompletedOn, clock.Today);
                return await repo.UpdateAsync(id, req.CompletedOn, req.Score, clock.Today, ct) is { } c ? TypedResults.Ok(c) : TypedResults.NotFound();
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Corregge data o punteggio (la versione precedente resta nello storico temporale)");

        return api;
    }

    private static void EnsureValidCompletionDate(DateOnly completedOn, DateOnly today)
    {
        if (completedOn > today)
        {
            throw new UnprocessableException("La data di completamento non può essere futura.");
        }

        if (completedOn < MinDate)
        {
            throw new UnprocessableException("La data di completamento non può essere precedente al 2000.");
        }
    }
}
