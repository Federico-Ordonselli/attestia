using System.Net;
using System.Net.Http.Json;
using Attestia.Tests.Infrastructure;

namespace Attestia.Tests.Api;

/// <summary>Chi puo' fare cosa. In particolare: un Employee non legge MAI i dati di un altro dipendente.</summary>
[Trait("Category", "Integration")]
public sealed class AuthorizationTests(ApiScenario scenario) : IClassFixture<ApiScenario>
{
    private readonly ApiFactory _api = scenario.Factory;

    public static TheoryData<string, string> ProtectedReads => new()
    {
        { "GET", "/api/courses" },
        { "GET", "/api/employees" },
        { "GET", "/api/employees/A1/certificates" },
        { "GET", "/api/departments" },
        { "GET", "/api/expirations" },
        { "GET", "/api/summary/departments" },
        { "GET", "/api/me" },
        { "GET", "/api/me/certificates" },
    };

    [Theory]
    [MemberData(nameof(ProtectedReads))]
    public async Task Anonymous_requests_get_401_problem_details(string method, string url)
    {
        using var client = _api.ClientAs(null);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_openapi_are_anonymous()
    {
        using var client = _api.ClientAs(null);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Theory]
    [InlineData("/api/employees/A2/certificates")] // un altro dipendente
    [InlineData("/api/employees/A1/certificates")] // anche se stesso: l'endpoint per matricola e' solo HR
    [InlineData("/api/employees?query=A")]
    [InlineData("/api/expirations")]
    [InlineData("/api/summary/departments")]
    [InlineData("/api/courses")]
    public async Task Employee_cannot_read_other_peoples_data_or_hr_reports(string url)
    {
        using var client = _api.ClientAs("A1", "Employee");

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_reads_only_own_certificates_with_number_taken_from_the_claim()
    {
        using var client = _api.ClientAs("A1", "Employee");

        // Un parametro "number" in query non ha alcun effetto: l'endpoint non lo legge.
        var history = await client.GetFromJsonAsync<HistoryDto>("/api/me/certificates?number=A2");

        Assert.Equal("A1", history!.Employee.EmployeeNumber);
        Assert.Equal(["C12", "C24"], history.Certificates.Select(c => c.CourseCode).Order());
    }

    [Fact]
    public async Task Unknown_role_is_forbidden_everywhere()
    {
        using var client = _api.ClientAs("A1", "Guest");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("Admin")]
    public async Task Hr_and_admin_can_read_reports(string role)
    {
        using var client = _api.ClientAs("B1", role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/expirations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/employees/A2/certificates")).StatusCode);
    }

    [Fact]
    public async Task Hr_cannot_manage_courses()
    {
        using var client = _api.ClientAs("B1", "HR");

        using var create = await client.PostAsJsonAsync("/api/courses", new { code = "HRNO", title = "Corso vietato", category = "Test", isMandatory = false });
        using var delete = await client.DeleteAsync("/api/courses/1");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_record_certificates()
    {
        using var client = _api.ClientAs("A1", "Employee");

        using var response = await client.PostAsJsonAsync("/api/certificates", new { employeeNumber = "A1", courseCode = "C12", completedOn = "2026-06-01" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record HistoryDto(EmployeeDto Employee, List<HistoryEntryDto> Certificates);

    private sealed record EmployeeDto(string EmployeeNumber);

    private sealed record HistoryEntryDto(string CourseCode, string Status);
}
