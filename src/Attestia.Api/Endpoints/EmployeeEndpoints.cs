using System.ComponentModel.DataAnnotations;
using Attestia.Api.Auth;
using Attestia.Core.Models;
using Attestia.Core.Time;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Attestia.Api.Endpoints;

internal static class EmployeeEndpoints
{
    public static RouteGroupBuilder MapEmployees(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/employees").WithTags("Employees").RequireAuthorization(Policies.Hr);

        g.MapGet("/", async (IEmployeeRepository repo, CancellationToken ct,
                    [StringLength(100)] string? query = null, [StringLength(100)] string? department = null) =>
                TypedResults.Ok(await repo.SearchAsync(query, department, ct)))
            .WithName("searchEmployees")
            .WithSummary("Ricerca dipendenti per matricola o nome (max 50 risultati)");

        g.MapGet("/{number}/certificates", async Task<Results<Ok<EmployeeHistory>, NotFound>> (
                    [RegularExpression("^[A-Z0-9-]{2,20}$")] string number,
                    IEmployeeRepository employees, ICertificateRepository certificates, IBusinessClock clock, CancellationToken ct) =>
                await LoadHistory(number, employees, certificates, clock, ct) is { } h ? TypedResults.Ok(h) : TypedResults.NotFound())
            .WithName("getEmployeeHistory")
            .WithSummary("Storico attestati di un dipendente (solo HR/Admin)");

        api.MapGet("/departments", async (IEmployeeRepository repo, CancellationToken ct) => TypedResults.Ok(await repo.DepartmentsAsync(ct)))
            .WithTags("Employees")
            .RequireAuthorization(Policies.Hr)
            .WithName("listDepartments")
            .WithSummary("Reparti con almeno un dipendente attivo");

        return api;
    }

    internal static async Task<EmployeeHistory?> LoadHistory(string number, IEmployeeRepository employees, ICertificateRepository certificates,
        IBusinessClock clock, CancellationToken ct)
    {
        var employee = await employees.GetByNumberAsync(number, ct);
        if (employee is null)
        {
            return null;
        }

        return new EmployeeHistory { Employee = employee, Certificates = await certificates.HistoryAsync(number, clock.Today, ct) };
    }
}
