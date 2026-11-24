using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Attestia.Api.Auth;
using Attestia.Core.Models;
using Attestia.Core.Security;
using Attestia.Core.Time;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Attestia.Api.Endpoints;

internal static class MeEndpoints
{
    public static RouteGroupBuilder MapMe(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/me").WithTags("Utente corrente").RequireAuthorization(Policies.AnyUser);

        g.MapGet("/", async (HttpContext http, IAntiforgery antiforgery, ClaimsPrincipal user, IEmployeeRepository employees, CancellationToken ct) =>
            {
                // Angular chiama /api/me all'avvio: e' il momento giusto per emettere il token CSRF.
                CsrfProtection.IssueToken(http, antiforgery);
                var number = user.EmployeeNumber();
                var employee = await employees.GetByNumberAsync(number, ct);
                var displayName = user.FindFirstValue(AttestiaClaims.DisplayName)
                    ?? (employee is null ? number : $"{employee.FirstName} {employee.LastName}");
                return TypedResults.Ok(new MeResponse(number, user.Role(), displayName, employee?.Department));
            })
            .WithSummary("Identità e ruolo dell'utente autenticato (sessione SSO)");

        // Un dipendente legge solo i PROPRI attestati: la matricola arriva dal claim, non dalla URL.
        g.MapGet("/certificates", async Task<Results<Ok<EmployeeHistory>, NotFound>> (ClaimsPrincipal user,
                    IEmployeeRepository employees, ICertificateRepository certificates, IBusinessClock clock, CancellationToken ct) =>
                await EmployeeEndpoints.LoadHistory(user.EmployeeNumber(), employees, certificates, clock, ct) is { } h
                    ? TypedResults.Ok(h)
                    : TypedResults.NotFound())
            .WithSummary("Storico attestati dell'utente autenticato");

        return api;
    }
}
