using Attestia.Data.Migrations;
using Attestia.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Attestia.Tests.Database;

[Trait("Category", "Integration")]
public sealed class SchemaTests(ClassDatabase database) : IClassFixture<ClassDatabase>
{
    private readonly MsSqlFixture _db = database.Server;
    private readonly string _cs = database.ConnectionString;

    [Fact]
    public void Migrations_are_idempotent()
    {
        var second = DatabaseMigrator.Migrate(_cs, includeSeed: false, NullLogger.Instance);

        Assert.True(second.Succeeded);
        Assert.Empty(second.AppliedScripts);
    }

    [Theory]
    [InlineData("2024-03-10", 12, "2025-03-10")]
    [InlineData("2024-01-31", 1, "2024-02-29")] // fine mese: DATEADD tronca al 29 febbraio (anno bisestile)
    [InlineData("2023-01-31", 1, "2023-02-28")]
    [InlineData("2024-02-29", 12, "2025-02-28")]
    public async Task ExpiresOn_is_computed_from_validity_months(string completed, int months, string expected)
    {
        var key = $"{completed.Replace("-", "", StringComparison.Ordinal)[2..]}{months}";
        var emp = await Sql.AddEmployeeAsync(_cs, $"X{key}");
        var course = await Sql.AddCourseAsync(_cs, $"C{key}", months);
        var id = await Sql.AddCertificateAsync(_cs, emp, course, DateOnly.Parse(completed, System.Globalization.CultureInfo.InvariantCulture));

        var expiresOn = await Sql.ScalarAsync<DateTime>(_cs, "SELECT ExpiresOn FROM dbo.Certificates WHERE Id = @id", new { id });

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DateOnly.FromDateTime(expiresOn));
    }

    [Fact]
    public async Task Course_without_validity_produces_no_expiry()
    {
        var emp = await Sql.AddEmployeeAsync(_cs, "E9002");
        var course = await Sql.AddCourseAsync(_cs, "NOEXP", validityMonths: null);
        var id = await Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2020, 1, 1));

        var count = await Sql.ScalarAsync<int>(_cs, "SELECT COUNT(*) FROM dbo.Certificates WHERE Id = @id AND ExpiresOn IS NULL", new { id });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Changing_course_validity_does_not_rewrite_existing_expiry()
    {
        var emp = await Sql.AddEmployeeAsync(_cs, "E9003");
        var course = await Sql.AddCourseAsync(_cs, "SNAP", 12);
        var id = await Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2025, 1, 15));

        await Sql.ExecAsync(_cs, "UPDATE dbo.Courses SET ValidityMonths = 36 WHERE Id = @course", new { course });
        var expiresOn = await Sql.ScalarAsync<DateTime>(_cs, "SELECT ExpiresOn FROM dbo.Certificates WHERE Id = @id", new { id });

        Assert.Equal(new DateTime(2026, 1, 15), expiresOn);
    }

    [Fact]
    public async Task Certificates_table_keeps_system_versioned_history()
    {
        var emp = await Sql.AddEmployeeAsync(_cs, "E9004");
        var course = await Sql.AddCourseAsync(_cs, "HIST", 12);
        var id = await Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2025, 5, 1), score: 70);

        await Sql.ExecAsync(_cs, "EXEC dbo.usp_Certificates_Update @Id = @id, @CompletedOn = '2025-05-02', @Score = 95", new { id });

        var temporalType = await Sql.ScalarAsync<string>(_cs,
            "SELECT temporal_type_desc FROM sys.tables WHERE name = 'Certificates'");
        var versions = await Sql.QueryAsync<decimal>(_cs,
            "SELECT Score FROM dbo.Certificates FOR SYSTEM_TIME ALL WHERE Id = @id ORDER BY ValidFrom", new { id });
        var historyRows = await Sql.QueryAsync<decimal>(_cs, "SELECT Score FROM dbo.CertificatesHistory WHERE Id = @id", new { id });

        Assert.Equal("SYSTEM_VERSIONED_TEMPORAL_TABLE", temporalType);
        Assert.Equal([70m, 95m], versions);
        Assert.Equal([70m], historyRows);
    }

    [Fact]
    public async Task Same_employee_course_and_date_cannot_be_recorded_twice()
    {
        var emp = await Sql.AddEmployeeAsync(_cs, "E9005");
        var course = await Sql.AddCourseAsync(_cs, "UNIQ", 12);
        await Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2025, 6, 1));

        var ex = await Assert.ThrowsAsync<SqlException>(() => Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2025, 6, 1)));

        Assert.Equal(2627, ex.Number);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public async Task Score_outside_0_100_is_rejected_by_check_constraint(double score)
    {
        var emp = await Sql.AddEmployeeAsync(_cs, $"E91{(score < 0 ? 1 : 2)}");
        var course = await Sql.AddCourseAsync(_cs, $"SCORE{(score < 0 ? 1 : 2)}", 12);

        var ex = await Assert.ThrowsAsync<SqlException>(() => Sql.AddCertificateAsync(_cs, emp, course, new DateOnly(2025, 1, 1), (decimal)score));

        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task Import_source_requires_an_import_log()
    {
        var emp = await Sql.AddEmployeeAsync(_cs, "E9006");
        var course = await Sql.AddCourseAsync(_cs, "SRC", 12);

        var ex = await Assert.ThrowsAsync<SqlException>(() => Sql.ExecAsync(_cs, """
            INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Source)
            VALUES (@emp, @course, '2025-01-01', 12, 'XmlImport')
            """, new { emp, course }));

        Assert.Equal(547, ex.Number);
    }

    /// <summary>Esegue <paramref name="body"/> impersonando <paramref name="user"/>, con REVERT garantito anche in caso di errore.</summary>
    private Task<List<string>> AsUser(string user, string body) =>
        Sql.QueryAsync<string>(_cs + ";Pooling=false", $"""
            EXECUTE AS USER = '{user}';
            BEGIN TRY
                {body}
            END TRY
            BEGIN CATCH
                REVERT;
                THROW;
            END CATCH;
            REVERT;
            SELECT 'ok';
            """);

    [Fact]
    public async Task Portal_role_can_only_execute_the_history_procedure()
    {
        await Sql.AddEmployeeAsync(_cs, "E9007");
        await Sql.ExecAsync(_cs, "CREATE USER portal_probe WITHOUT LOGIN; ALTER ROLE attestia_portal ADD MEMBER portal_probe;");

        var rows = await AsUser("portal_probe", """
            CREATE TABLE #r (EmployeeNumber NVARCHAR(20), FirstName NVARCHAR(100), LastName NVARCHAR(100), Department NVARCHAR(100),
                CertificateId INT, CourseCode VARCHAR(20), CourseTitle NVARCHAR(200), Category NVARCHAR(100), IsMandatory BIT,
                CompletedOn DATE, ExpiresOn DATE, Score DECIMAL(5,2), Source VARCHAR(10), Status VARCHAR(12), IsCurrent BIT);
            INSERT INTO #r EXEC dbo.usp_EmployeeCertificateHistory @EmployeeNumber = 'E9007';
            """);
        var denied = await Assert.ThrowsAsync<SqlException>(() => AsUser("portal_probe", "SELECT COUNT(*) FROM dbo.Employees;"));
        var deniedProc = await Assert.ThrowsAsync<SqlException>(() => AsUser("portal_probe", "EXEC dbo.usp_DepartmentSummary;"));

        Assert.Equal(["ok"], rows);
        Assert.Equal(229, denied.Number);     // SELECT permission denied
        Assert.Equal(229, deniedProc.Number); // EXECUTE permission denied
    }

    [Fact]
    public async Task Api_role_cannot_read_tables_directly()
    {
        await Sql.ExecAsync(_cs, "CREATE USER api_probe WITHOUT LOGIN; ALTER ROLE attestia_api ADD MEMBER api_probe;");

        var denied = await Assert.ThrowsAsync<SqlException>(() => AsUser("api_probe", "SELECT COUNT(*) FROM dbo.Certificates;"));
        var allowed = await AsUser("api_probe", "EXEC dbo.usp_Departments_List;");

        Assert.Equal(229, denied.Number);
        Assert.NotEmpty(allowed); // primo result set = output della procedura, quindi EXECUTE e' consentito
    }

    [Fact]
    public async Task Seed_is_deterministic_and_has_a_realistic_mix()
    {
        var a = _db.CreateDatabase("seed_a", seed: true);
        var b = _db.CreateDatabase("seed_b", seed: true);
        const string fingerprint = """
            SELECT CHECKSUM_AGG(CHECKSUM(e.EmployeeNumber, e.LastName, e.Department, co.Code, c.CompletedOn, c.Score))
            FROM dbo.Certificates c JOIN dbo.Employees e ON e.Id = c.EmployeeId JOIN dbo.Courses co ON co.Id = c.CourseId
            """;

        var employees = await Sql.ScalarAsync<int>(a, "SELECT COUNT(*) FROM dbo.Employees");
        var departments = await Sql.ScalarAsync<int>(a, "SELECT COUNT(DISTINCT Department) FROM dbo.Employees");
        var courses = await Sql.ScalarAsync<int>(a, "SELECT COUNT(*) FROM dbo.Courses");
        var certificates = await Sql.ScalarAsync<int>(a, "SELECT COUNT(*) FROM dbo.Certificates");
        var statuses = await Sql.QueryAsync<string>(a, "SELECT DISTINCT Status FROM dbo.fn_ComplianceMatrix(dbo.fn_BusinessToday(), 60)");
        var fakeEmails = await Sql.ScalarAsync<int>(a, "SELECT COUNT(*) FROM dbo.Employees WHERE Email NOT LIKE '%@example.test'");

        Assert.Equal(60, employees);
        Assert.Equal(5, departments);
        Assert.Equal(8, courses);
        Assert.InRange(certificates, 270, 330);
        Assert.Equal(["Expired", "ExpiringSoon", "Missing", "Valid"], statuses.Order());
        Assert.Equal(0, fakeEmails);
        Assert.Equal(await Sql.ScalarAsync<int>(a, fingerprint), await Sql.ScalarAsync<int>(b, fingerprint));

        var reseed = DatabaseMigrator.Migrate(a, includeSeed: true, NullLogger.Instance);
        Assert.Empty(reseed.AppliedScripts);
    }
}
