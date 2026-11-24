using System.Data;
using Attestia.Core.Models;
using Attestia.Data.Infrastructure;
using Dapper;

namespace Attestia.Data.Repositories;

// Tutti i repository eseguono SOLO stored procedure (CommandType.StoredProcedure): vedi DECISIONS.md D02.

internal static class Proc
{
    public static CommandDefinition Cmd(string name, object? args, CancellationToken ct) =>
        new(name, args, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    public static DateTime Date(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
}

public sealed record CourseInput(string Code, string Title, string Category, int? ValidityMonths, bool IsMandatory);

public sealed record CourseUpdate(string Title, string Category, int? ValidityMonths, bool IsMandatory, bool IsActive);

public interface ICourseRepository
{
    Task<IReadOnlyList<Course>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Course?> GetAsync(int id, CancellationToken ct);
    Task<Course> InsertAsync(CourseInput input, CancellationToken ct);
    Task<Course?> UpdateAsync(int id, CourseUpdate input, CancellationToken ct);
    Task<bool> DeactivateAsync(int id, CancellationToken ct);
}

public sealed class CourseRepository(ISqlConnectionFactory db) : ICourseRepository
{
    public async Task<IReadOnlyList<Course>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<Course>(Proc.Cmd("dbo.usp_Courses_List", new { IncludeInactive = includeInactive }, ct))).AsList();
    }

    public async Task<Course?> GetAsync(int id, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<Course>(Proc.Cmd("dbo.usp_Courses_Get", new { Id = id }, ct));
    }

    public Task<Course> InsertAsync(CourseInput input, CancellationToken ct) =>
        SqlErrors.Translate(async () =>
        {
            await using var c = db.Create();
            return await c.QuerySingleAsync<Course>(Proc.Cmd("dbo.usp_Courses_Insert", new
            {
                input.Code,
                input.Title,
                input.Category,
                ValidityMonths = (short?)input.ValidityMonths,
                input.IsMandatory,
            }, ct));
        }, duplicateMessage: "Esiste già un corso con questo codice.");

    public async Task<Course?> UpdateAsync(int id, CourseUpdate input, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<Course>(Proc.Cmd("dbo.usp_Courses_Update", new
        {
            Id = id,
            input.Title,
            input.Category,
            ValidityMonths = (short?)input.ValidityMonths,
            input.IsMandatory,
            input.IsActive,
        }, ct));
    }

    public async Task<bool> DeactivateAsync(int id, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.ExecuteScalarAsync<int>(Proc.Cmd("dbo.usp_Courses_Deactivate", new { Id = id }, ct)) > 0;
    }
}

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> SearchAsync(string? query, string? department, CancellationToken ct);
    Task<Employee?> GetByNumberAsync(string employeeNumber, CancellationToken ct);
    Task<IReadOnlyList<string>> DepartmentsAsync(CancellationToken ct);
}

public sealed class EmployeeRepository(ISqlConnectionFactory db) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> SearchAsync(string? query, string? department, CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<Employee>(Proc.Cmd("dbo.usp_Employees_Search", new { Query = query, Department = department, Top = 50 }, ct))).AsList();
    }

    public async Task<Employee?> GetByNumberAsync(string employeeNumber, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<Employee>(Proc.Cmd("dbo.usp_Employees_GetByNumber", new { EmployeeNumber = employeeNumber }, ct));
    }

    public async Task<IReadOnlyList<string>> DepartmentsAsync(CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<string>(Proc.Cmd("dbo.usp_Departments_List", null, ct))).AsList();
    }
}

public sealed record CertificateInput(string EmployeeNumber, string CourseCode, DateOnly CompletedOn, decimal? Score);

public interface ICertificateRepository
{
    Task<CertificateRecord?> GetAsync(int id, DateOnly asOf, CancellationToken ct);
    Task<CertificateRecord> InsertAsync(CertificateInput input, DateOnly asOf, CancellationToken ct);
    Task<CertificateRecord?> UpdateAsync(int id, DateOnly completedOn, decimal? score, DateOnly asOf, CancellationToken ct);
    Task<IReadOnlyList<CertificateHistoryEntry>> HistoryAsync(string employeeNumber, DateOnly asOf, CancellationToken ct);
}

public sealed class CertificateRepository(ISqlConnectionFactory db) : ICertificateRepository
{
    private const string Duplicate = "Esiste già un attestato per questo dipendente, corso e data.";

    public async Task<CertificateRecord?> GetAsync(int id, DateOnly asOf, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<CertificateRecord>(Proc.Cmd("dbo.usp_Certificates_Get", new { Id = id, AsOf = Proc.Date(asOf) }, ct));
    }

    public Task<CertificateRecord> InsertAsync(CertificateInput input, DateOnly asOf, CancellationToken ct) =>
        SqlErrors.Translate(async () =>
        {
            await using var c = db.Create();
            var id = await c.QuerySingleAsync<int>(Proc.Cmd("dbo.usp_Certificates_Insert", new
            {
                input.EmployeeNumber,
                input.CourseCode,
                CompletedOn = Proc.Date(input.CompletedOn),
                input.Score,
            }, ct));
            return (await GetAsync(id, asOf, ct))!;
        }, Duplicate);

    public Task<CertificateRecord?> UpdateAsync(int id, DateOnly completedOn, decimal? score, DateOnly asOf, CancellationToken ct) =>
        SqlErrors.Translate(async () =>
        {
            await using var c = db.Create();
            var updatedId = await c.QuerySingleOrDefaultAsync<int?>(Proc.Cmd("dbo.usp_Certificates_Update", new
            {
                Id = id,
                CompletedOn = Proc.Date(completedOn),
                Score = score,
            }, ct));
            return updatedId is null ? null : await GetAsync(updatedId.Value, asOf, ct);
        }, Duplicate);

    public async Task<IReadOnlyList<CertificateHistoryEntry>> HistoryAsync(string employeeNumber, DateOnly asOf, CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<CertificateHistoryEntry>(Proc.Cmd("dbo.usp_EmployeeCertificateHistory",
            new { EmployeeNumber = employeeNumber, AsOf = Proc.Date(asOf) }, ct))).AsList();
    }
}

public interface IReportRepository
{
    Task<PagedResult<ExpiringCertificate>> ExpiringAsync(int withinDays, string? department, int page, int? pageSize, DateOnly asOf, CancellationToken ct);
    Task<IReadOnlyList<DepartmentSummary>> DepartmentSummaryAsync(DateOnly asOf, CancellationToken ct);
    Task<int> ExpiringSoonDaysAsync(CancellationToken ct);
}

public sealed class ReportRepository(ISqlConnectionFactory db) : IReportRepository
{
    public async Task<PagedResult<ExpiringCertificate>> ExpiringAsync(int withinDays, string? department, int page, int? pageSize, DateOnly asOf, CancellationToken ct)
    {
        var p = new DynamicParameters(new
        {
            WithinDays = withinDays,
            Department = string.IsNullOrWhiteSpace(department) ? null : department,
            Page = page,
            PageSize = pageSize,
            AsOf = Proc.Date(asOf),
        });
        p.Add("TotalCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await using var c = db.Create();
        var rows = (await c.QueryAsync<ExpiringCertificate>(Proc.Cmd("dbo.usp_ExpiringCertificates", p, ct))).AsList();
        var total = p.Get<int>("TotalCount");
        return new PagedResult<ExpiringCertificate>(rows, page, pageSize ?? total, total);
    }

    public async Task<IReadOnlyList<DepartmentSummary>> DepartmentSummaryAsync(DateOnly asOf, CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<DepartmentSummary>(Proc.Cmd("dbo.usp_DepartmentSummary", new { AsOf = Proc.Date(asOf) }, ct))).AsList();
    }

    public async Task<int> ExpiringSoonDaysAsync(CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.ExecuteScalarAsync<int>(Proc.Cmd("dbo.usp_AppSettings_Get", null, ct));
    }
}
