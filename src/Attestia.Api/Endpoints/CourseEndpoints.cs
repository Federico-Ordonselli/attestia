using Attestia.Api.Auth;
using Attestia.Core.Models;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Attestia.Api.Endpoints;

internal static class CourseEndpoints
{
    public static RouteGroupBuilder MapCourses(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/courses").WithTags("Courses");

        g.MapGet("/", async (ICourseRepository repo, CancellationToken ct, bool includeInactive = false) =>
                TypedResults.Ok(await repo.ListAsync(includeInactive, ct)))
            .RequireAuthorization(Policies.Hr)
            .WithName("listCourses")
            .WithSummary("Elenco dei corsi");

        g.MapGet("/{id:int}", async Task<Results<Ok<Course>, NotFound>> (int id, ICourseRepository repo, CancellationToken ct) =>
                await repo.GetAsync(id, ct) is { } c ? TypedResults.Ok(c) : TypedResults.NotFound())
            .RequireAuthorization(Policies.Hr)
            .WithName("getCourse")
            .WithSummary("Dettaglio di un corso");

        g.MapPost("/", async (CreateCourseRequest req, ICourseRepository repo, CancellationToken ct) =>
            {
                var course = await repo.InsertAsync(new CourseInput(req.Code, req.Title.Trim(), req.Category.Trim(), req.ValidityMonths, req.IsMandatory), ct);
                return TypedResults.CreatedAtRoute(course, "getCourse", new { id = course.Id });
            })
            .RequireAuthorization(Policies.Admin)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("createCourse")
            .WithSummary("Crea un corso (solo Admin)");

        g.MapPut("/{id:int}", async Task<Results<Ok<Course>, NotFound>> (int id, UpdateCourseRequest req, ICourseRepository repo, CancellationToken ct) =>
                await repo.UpdateAsync(id, new CourseUpdate(req.Title.Trim(), req.Category.Trim(), req.ValidityMonths, req.IsMandatory, req.IsActive), ct) is { } c
                    ? TypedResults.Ok(c)
                    : TypedResults.NotFound())
            .RequireAuthorization(Policies.Admin)
            .WithName("updateCourse")
            .WithSummary("Modifica un corso (solo Admin; il codice non cambia)");

        g.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, ICourseRepository repo, CancellationToken ct) =>
                await repo.DeactivateAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(Policies.Admin)
            .WithName("deactivateCourse")
            .WithSummary("Disattiva un corso (solo Admin). Gli attestati esistenti restano.");

        return api;
    }
}
