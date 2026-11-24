using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Attestia.Api.Auth;
using Attestia.Api.XmlExchange;
using Attestia.Api.Infrastructure;
using Attestia.Core.Time;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Attestia.Api.Endpoints;

public sealed record ImportLogResponse(int Id, string FileName, string Sha256, DateTime ImportedAt, string ImportedBy, int TotalRows, int OkRows,
    int RejectedRows, IReadOnlyList<ImportLogError> Errors);

public sealed record ImportLogError(int Line, string? EmployeeNumber, string? CourseCode, DateOnly? CompletedOn, string? Reason);

internal static class ImportEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapImportsAndExports(this RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").WithTags("Import XML").RequireAuthorization(Policies.Hr);

        imports.MapPost("/certificates", async Task<Results<Ok<ImportReport>, ProblemHttpResult>> (
                    IFormFile file, ClaimsPrincipal user, ImportService service, CancellationToken ct, bool dryRun = false) =>
            {
                if (file.Length == 0)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "File mancante", detail: "Il file è vuoto.");
                }

                if (file.Length > ImportService.MaxFileBytes)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "File troppo grande",
                        detail: "La dimensione massima è 2 MB.");
                }

                if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Formato non supportato",
                        detail: "È accettato solo un file .xml nel formato urn:attestia:import:v1.");
                }

                var content = new byte[file.Length];
                await using (var stream = file.OpenReadStream())
                {
                    await stream.ReadExactlyAsync(content, ct);
                }

                try
                {
                    return TypedResults.Ok(await service.ImportAsync(content, file.FileName, user.EmployeeNumber(), dryRun, ct));
                }
                catch (SchemaValidationException ex)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "File non conforme allo schema",
                        detail: "Il file non rispetta lo schema urn:attestia:import:v1: nessuna riga è stata salvata.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["schemaErrors"] = ex.Issues.Take(100).Select(i => new { line = i.LineNumber, position = i.LinePosition, message = i.Message }).ToList(),
                        });
                }
            })
            // La validazione antiforgery integrata per i form e' sostituita da UseCsrfProtection, che copre
            // con la stessa regola sia il JSON sia il multipart (header X-XSRF-TOKEN obbligatorio).
            .DisableAntiforgery()
            .WithFormOptions(multipartBodyLengthLimit: ImportService.MaxFileBytes + 64 * 1024)
            .RequireRateLimiting(RateLimits.Upload)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Importa i completamenti da un XML dell'LMS (dryRun=true: solo anteprima, nulla viene salvato)");

        imports.MapGet("/", async (IImportRepository repo, CancellationToken ct) =>
                TypedResults.Ok((await repo.ListAsync(50, ct)).Select(ToResponse).ToList()))
            .WithSummary("Storico degli import (ultimi 50)");

        api.MapGet("/exports/expirations.xml", async (HttpContext http, IReportRepository repo, IBusinessClock clock, CancellationToken ct,
                    [Range(0, 3650)] int withinDays = 60, [StringLength(100)] string? department = null) =>
            {
                var today = clock.Today;
                var result = await repo.ExpiringAsync(withinDays, department, 1, null, today, ct);
                http.Response.ContentType = "application/xml; charset=utf-8";
                http.Response.Headers.ContentDisposition = $"attachment; filename=\"scadenze-{today:yyyy-MM-dd}.xml\"";
                await ExpirationsXmlWriter.WriteAsync(http.Response.Body, result.Items, clock.UtcNow, today, withinDays, department, ct);
            })
            .WithTags("Import XML")
            .RequireAuthorization(Policies.Hr)
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .WithSummary("Export XML (urn:attestia:export:v1) delle scadenze");

        return api;
    }

    private static ImportLogResponse ToResponse(ImportLogEntry e) => new(e.Id, e.FileName, e.Sha256,
        DateTime.SpecifyKind(e.ImportedAt, DateTimeKind.Utc), e.ImportedBy, e.TotalRows, e.OkRows, e.RejectedRows,
        e.ErrorsJson is null ? [] : JsonSerializer.Deserialize<List<ImportLogError>>(e.ErrorsJson, Web) ?? []);
}
