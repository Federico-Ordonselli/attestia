using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attestia.Data.Repositories;
using Attestia.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Attestia.Tests.Api;

[Trait("Category", "Integration")]
public sealed class EndpointTests(ApiScenario scenario) : IClassFixture<ApiScenario>
{
    private readonly ApiFactory _api = scenario.Factory;

    private HttpClient Hr => _api.ClientAs("B1", "HR");

    private HttpClient Admin => _api.ClientAs("B2", "Admin");

    [Fact]
    public async Task Expirations_are_paginated_server_side_and_use_the_business_clock()
    {
        using var client = Hr;

        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/expirations?withinDays=60&department=Alfa&pageSize=3");
        var page2 = await client.GetFromJsonAsync<JsonElement>("/api/expirations?withinDays=60&department=Alfa&pageSize=3&page=2");

        Assert.Equal(4, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(["A1", "A3", "A2"], page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("employeeNumber").GetString()));
        Assert.Equal("Expired", page1.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("2026-06-10", page1.GetProperty("items")[0].GetProperty("expiresOn").GetString());
        Assert.Single(page2.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("/api/expirations?withinDays=-1")]
    [InlineData("/api/expirations?pageSize=1000")]
    [InlineData("/api/expirations?page=0")]
    [InlineData("/api/employees/a%20b/certificates")]
    public async Task Invalid_query_values_return_400_validation_problem(string url)
    {
        using var client = Hr;

        using var response = await client.GetAsync(url);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(problem!.Errors);
    }

    [Fact]
    public async Task Department_summary_counts_statuses()
    {
        using var client = Hr;

        var rows = await client.GetFromJsonAsync<JsonElement>("/api/summary/departments");

        var alfa = rows.EnumerateArray().Single(r => r.GetProperty("department").GetString() == "Alfa");
        Assert.Equal(3, alfa.GetProperty("expiringSoonCount").GetInt32());
        Assert.Equal(1, alfa.GetProperty("expiredCount").GetInt32());
    }

    [Fact]
    public async Task Employee_history_returns_404_for_unknown_number()
    {
        using var client = Hr;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/employees/ZZ99/certificates")).StatusCode);
    }

    [Fact]
    public async Task Admin_course_lifecycle_create_update_deactivate()
    {
        using var client = Admin;

        using var created = await client.PostAsJsonAsync("/api/courses", new { code = "LIFE-1", title = "Corso di prova", category = "Test", validityMonths = 12, isMandatory = true });
        var course = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetInt32();

        using var duplicate = await client.PostAsJsonAsync("/api/courses", new { code = "LIFE-1", title = "Duplicato", category = "Test", isMandatory = false });
        using var updated = await client.PutAsJsonAsync($"/api/courses/{id}", new { title = "Corso rinominato", category = "Test", validityMonths = 24, isMandatory = false, isActive = true });
        using var deleted = await client.DeleteAsync($"/api/courses/{id}");
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/courses/{id}");
        var activeList = await client.GetFromJsonAsync<JsonElement>("/api/courses");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/api/courses/{id}", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Corso rinominato", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(after.GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain(activeList.EnumerateArray(), c => c.GetProperty("id").GetInt32() == id);
    }

    [Theory]
    [InlineData("lowercase")]
    [InlineData("A")]
    [InlineData("WITH SPACE")]
    public async Task Invalid_course_code_is_rejected_with_field_error(string code)
    {
        using var client = Admin;

        using var response = await client.PostAsJsonAsync("/api/courses", new { code, title = "Titolo valido", category = "Test", isMandatory = false });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors.Keys, k => k.Equals("Code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Hr_records_a_certificate_and_gets_status_and_expiry()
    {
        using var client = Hr;

        using var response = await client.PostAsJsonAsync("/api/certificates", new { employeeNumber = "B2", courseCode = "C12", completedOn = "2025-07-01", score = 88.5 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("2026-07-01", body.GetProperty("expiresOn").GetString());
        Assert.Equal("ExpiringSoon", body.GetProperty("status").GetString());
        Assert.Equal("Manual", body.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("A1", "C12", "2026-06-16", HttpStatusCode.UnprocessableEntity)] // domani: data futura
    [InlineData("A1", "C12", "1999-12-31", HttpStatusCode.UnprocessableEntity)]
    [InlineData("ZZ99", "C12", "2026-01-01", HttpStatusCode.UnprocessableEntity)] // matricola inesistente
    [InlineData("A1", "NOPE", "2026-01-01", HttpStatusCode.UnprocessableEntity)]  // corso inesistente
    [InlineData("A1", "OLD", "2026-01-01", HttpStatusCode.UnprocessableEntity)]   // corso disattivato
    [InlineData("B3", "C12", "2026-01-01", HttpStatusCode.UnprocessableEntity)]   // dipendente non attivo
    [InlineData("A1", "C12", "2025-06-10", HttpStatusCode.Conflict)]              // gia' registrato
    public async Task Invalid_certificates_are_rejected_with_problem_details(string employee, string course, string date, HttpStatusCode expected)
    {
        using var client = Hr;

        using var response = await client.PostAsJsonAsync("/api/certificates", new { employeeNumber = employee, courseCode = course, completedOn = date });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(expected, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(problem!.Detail));
    }

    [Fact]
    public async Task Updating_a_missing_certificate_returns_404()
    {
        using var client = Hr;

        using var response = await client.PutAsJsonAsync("/api/certificates/999999", new { completedOn = "2026-01-01", score = 50 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_identity_from_claims()
    {
        using var client = _api.ClientAs("A2", "Employee");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");

        Assert.Equal("A2", me.GetProperty("employeeNumber").GetString());
        Assert.Equal("Employee", me.GetProperty("role").GetString());
        Assert.Equal("Alfa", me.GetProperty("department").GetString());
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var client = Hr;

        using var response = await client.GetAsync("/api/departments");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task Unexpected_errors_return_generic_500_without_internal_details()
    {
        using var failing = new ApiFactory(scenario.Database.ConnectionString, s => s.AddScoped<IReportRepository, ExplodingReports>());
        using var client = failing.ClientAs("B1", "HR");

        using var response = await client.GetAsync("/api/summary/departments");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("segreto", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExplodingReports", text, StringComparison.Ordinal);
        Assert.Contains("traceId", text, StringComparison.Ordinal);
    }

    private sealed class ExplodingReports : IReportRepository
    {
        public Task<Attestia.Core.Models.PagedResult<Attestia.Core.Models.ExpiringCertificate>> ExpiringAsync(int withinDays, string? department, int page, int? pageSize, DateOnly asOf, CancellationToken ct) =>
            throw new InvalidOperationException("dettaglio segreto: Server=db;Password=x");

        public Task<IReadOnlyList<Attestia.Core.Models.DepartmentSummary>> DepartmentSummaryAsync(DateOnly asOf, CancellationToken ct) =>
            throw new InvalidOperationException("dettaglio segreto: Server=db;Password=x");

        public Task<int> ExpiringSoonDaysAsync(CancellationToken ct) => Task.FromResult(60);
    }
}
