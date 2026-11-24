using System.Data;
using Attestia.Tests.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Attestia.Tests.Database;

/// <summary>
/// Scenario fisso (AsOf = 15/06/2026, soglia 60 giorni) condiviso dai test delle procedure di report.
/// </summary>
public sealed class ReportScenario
{
    public static readonly DateOnly AsOf = new(2026, 6, 15);

    public ReportScenario(MsSqlFixture server)
    {
        Infrastructure.ClassDatabase db = new(server);
        ConnectionString = db.ConnectionString;
        Seed().GetAwaiter().GetResult();
    }

    public string ConnectionString { get; }

    private async Task Seed()
    {
        var cs = ConnectionString;
        var c12 = await Sql.AddCourseAsync(cs, "C12", 12, mandatory: true);
        var c24 = await Sql.AddCourseAsync(cs, "C24", 24);
        var noExp = await Sql.AddCourseAsync(cs, "NOEXP", null);
        var retired = await Sql.AddCourseAsync(cs, "OLD", 12, mandatory: true, active: false);

        var a1 = await Sql.AddEmployeeAsync(cs, "A1", "Alfa");
        var a2 = await Sql.AddEmployeeAsync(cs, "A2", "Alfa");
        var a3 = await Sql.AddEmployeeAsync(cs, "A3", "Alfa");
        var b1 = await Sql.AddEmployeeAsync(cs, "B1", "Beta");
        var b2 = await Sql.AddEmployeeAsync(cs, "B2", "Beta");
        var b3 = await Sql.AddEmployeeAsync(cs, "B3", "Beta", active: false);

        await Sql.AddCertificateAsync(cs, a1, c12, new DateOnly(2025, 6, 10));   // scade 10/06/2026 -> Expired
        await Sql.AddCertificateAsync(cs, a1, c24, new DateOnly(2024, 8, 1));    // scade 01/08/2026 -> ExpiringSoon (47 gg)
        await Sql.AddCertificateAsync(cs, a2, c12, new DateOnly(2024, 7, 1));    // superato dal rinnovo
        await Sql.AddCertificateAsync(cs, a2, c12, new DateOnly(2025, 7, 15));   // scade 15/07/2026 -> ExpiringSoon (30 gg)
        await Sql.AddCertificateAsync(cs, a3, c12, new DateOnly(2025, 6, 15));   // scade oggi -> ancora valido, ExpiringSoon (0 gg)
        await Sql.AddCertificateAsync(cs, a3, retired, new DateOnly(2020, 1, 1)); // corso disattivato: ignorato nei report
        await Sql.AddCertificateAsync(cs, b1, c12, new DateOnly(2026, 1, 1));    // scade 01/01/2027 -> Valid
        await Sql.AddCertificateAsync(cs, b2, noExp, new DateOnly(2019, 3, 3));  // non scade -> Valid; C12 mancante
        await Sql.AddCertificateAsync(cs, b3, c12, new DateOnly(2020, 1, 1));    // dipendente non attivo: escluso
    }
}

[Trait("Category", "Integration")]
public sealed class ReportProcedureTests(ReportScenario scenario) : IClassFixture<ReportScenario>
{
    private readonly string _cs = scenario.ConnectionString;

    private sealed record ExpiringRow
    {
        public string EmployeeNumber { get; init; } = "";
        public string Department { get; init; } = "";
        public string CourseCode { get; init; } = "";
        public DateTime ExpiresOn { get; init; }
        public int DaysToExpiry { get; init; }
        public string Status { get; init; } = "";
    }

    private async Task<(List<ExpiringRow> Rows, int Total)> Expiring(int withinDays, string? department = null, int page = 1, int? pageSize = 20)
    {
        await using var c = new SqlConnection(_cs);
        var p = new DynamicParameters(new { WithinDays = withinDays, Department = department, Page = page, PageSize = pageSize, AsOf = ReportScenario.AsOf.ToDateTime(TimeOnly.MinValue), WarningDays = 60 });
        p.Add("TotalCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
        var rows = (await c.QueryAsync<ExpiringRow>("dbo.usp_ExpiringCertificates", p, commandType: CommandType.StoredProcedure)).AsList();
        return (rows, p.Get<int>("TotalCount"));
    }

    [Fact]
    public async Task Expiring_lists_expired_and_expiring_latest_certificates_ordered_by_date()
    {
        var (rows, total) = await Expiring(60);

        Assert.Equal(4, total);
        Assert.Equal(
            [("A1", "C12", "Expired", -5), ("A3", "C12", "ExpiringSoon", 0), ("A2", "C12", "ExpiringSoon", 30), ("A1", "C24", "ExpiringSoon", 47)],
            rows.Select(r => (r.EmployeeNumber, r.CourseCode, r.Status, r.DaysToExpiry)));
    }

    [Fact]
    public async Task Expiring_with_zero_days_includes_today_and_the_past()
    {
        var (rows, _) = await Expiring(0);

        Assert.Equal(["A1", "A3"], rows.Select(r => r.EmployeeNumber));
    }

    [Fact]
    public async Task Expiring_filters_by_department_and_paginates_with_total()
    {
        var (beta, betaTotal) = await Expiring(365, "Beta");
        var (page2, total) = await Expiring(60, page: 2, pageSize: 3);

        Assert.Equal(["B1"], beta.Select(r => r.EmployeeNumber));
        Assert.Equal(1, betaTotal);
        Assert.Equal(4, total);
        Assert.Equal(["C24"], page2.Select(r => r.CourseCode));
    }

    [Fact]
    public async Task Expiring_without_page_size_returns_everything()
    {
        var (rows, total) = await Expiring(3650, pageSize: null);

        Assert.Equal(5, rows.Count);
        Assert.Equal(5, total);
    }

    [Theory]
    [InlineData(-1, 1, 20, 50001)]
    [InlineData(30, 0, 20, 50002)]
    [InlineData(30, 1, 501, 50003)]
    public async Task Expiring_rejects_invalid_arguments(int withinDays, int page, int pageSize, int expectedError)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => Expiring(withinDays, page: page, pageSize: pageSize));

        Assert.Equal(expectedError, ex.Number);
    }

    private sealed record HistoryRow
    {
        public string EmployeeNumber { get; init; } = "";
        public int? CertificateId { get; init; }
        public string CourseCode { get; init; } = "";
        public DateTime? CompletedOn { get; init; }
        public string Status { get; init; } = "";
        public bool IsCurrent { get; init; }
        public bool IsMandatory { get; init; }
    }

    private Task<List<HistoryRow>> History(string number) =>
        Sql.ProcAsync<HistoryRow>(_cs, "dbo.usp_EmployeeCertificateHistory",
            new { EmployeeNumber = number, AsOf = ReportScenario.AsOf.ToDateTime(TimeOnly.MinValue), WarningDays = 60 });

    [Fact]
    public async Task History_contains_superseded_certificates_flagged_as_not_current()
    {
        var rows = await History("A2");

        Assert.Equal(
            [(new DateTime(2025, 7, 15), "ExpiringSoon", true), (new DateTime(2024, 7, 1), "Expired", false)],
            rows.Select(r => (r.CompletedOn!.Value, r.Status, r.IsCurrent)));
    }

    [Fact]
    public async Task History_adds_a_missing_row_for_each_mandatory_course_never_completed()
    {
        var rows = await History("B2");

        var missing = Assert.Single(rows, r => r.Status == "Missing");
        Assert.Equal("C12", missing.CourseCode);
        Assert.Null(missing.CertificateId);
        Assert.Contains(rows, r => r.CourseCode == "NOEXP" && r.Status == "Valid");
    }

    [Fact]
    public async Task History_of_unknown_employee_is_empty()
    {
        Assert.Empty(await History("ZZ99"));
    }

    private sealed record SummaryRow
    {
        public string Department { get; init; } = "";
        public int EmployeeCount { get; init; }
        public int ValidCount { get; init; }
        public int ExpiringSoonCount { get; init; }
        public int ExpiredCount { get; init; }
        public int MissingCount { get; init; }
    }

    [Fact]
    public async Task Department_summary_counts_each_status_for_active_employees_and_courses()
    {
        var rows = await Sql.ProcAsync<SummaryRow>(_cs, "dbo.usp_DepartmentSummary",
            new { AsOf = ReportScenario.AsOf.ToDateTime(TimeOnly.MinValue), WarningDays = 60 });

        Assert.Equal(
            [("Alfa", 3, 0, 3, 1, 0), ("Beta", 2, 2, 0, 0, 1)],
            rows.Select(r => (r.Department, r.EmployeeCount, r.ValidCount, r.ExpiringSoonCount, r.ExpiredCount, r.MissingCount)));
    }

    private sealed record EmployeeRow
    {
        public string EmployeeNumber { get; init; } = "";
        public string LastName { get; init; } = "";
    }

    [Theory]
    [InlineData("A", null, new[] { "A1", "A2", "A3" })]
    [InlineData("cognomeb", null, new[] { "B1", "B2", "B3" })]
    [InlineData(null, "Beta", new[] { "B1", "B2", "B3" })]
    [InlineData("%", null, new string[0])]   // i caratteri jolly sono letterali
    [InlineData("_1", null, new string[0])]
    public async Task Employee_search_matches_number_and_name_and_escapes_wildcards(string? query, string? department, string[] expected)
    {
        var rows = await Sql.ProcAsync<EmployeeRow>(_cs, "dbo.usp_Employees_Search", new { Query = query, Department = department });

        Assert.Equal(expected, rows.Select(r => r.EmployeeNumber).Order());
    }
}
