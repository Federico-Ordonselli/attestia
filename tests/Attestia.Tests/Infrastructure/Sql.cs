using Dapper;
using Microsoft.Data.SqlClient;

namespace Attestia.Tests.Infrastructure;

/// <summary>Piccoli helper per preparare dati nei test di integrazione (come sa, fuori dall'API).</summary>
public static class Sql
{
    public static async Task<T> ScalarAsync<T>(string cs, string sql, object? param = null)
    {
        await using var c = new SqlConnection(cs);
        return await c.ExecuteScalarAsync<T>(sql, param) ?? throw new InvalidOperationException("Risultato NULL inatteso.");
    }

    public static async Task<int> ExecAsync(string cs, string sql, object? param = null)
    {
        await using var c = new SqlConnection(cs);
        return await c.ExecuteAsync(sql, param);
    }

    public static async Task<List<T>> QueryAsync<T>(string cs, string sql, object? param = null)
    {
        await using var c = new SqlConnection(cs);
        return (await c.QueryAsync<T>(sql, param)).AsList();
    }

    public static async Task<List<T>> ProcAsync<T>(string cs, string proc, object? param = null)
    {
        await using var c = new SqlConnection(cs);
        return (await c.QueryAsync<T>(proc, param, commandType: System.Data.CommandType.StoredProcedure)).AsList();
    }

    public static Task<int> AddEmployeeAsync(string cs, string number, string department = "Produzione", bool active = true) =>
        ScalarAsync<int>(cs, """
            INSERT INTO dbo.Employees (EmployeeNumber, FirstName, LastName, Email, Department, Role, IsActive)
            OUTPUT inserted.Id VALUES (@number, N'Nome', N'Cognome' + @number, LOWER(@number) + '@example.test', @department, 'Employee', @active)
            """, new { number, department, active });

    public static Task<int> AddCourseAsync(string cs, string code, int? validityMonths, bool mandatory = false, bool active = true) =>
        ScalarAsync<int>(cs, """
            INSERT INTO dbo.Courses (Code, Title, Category, ValidityMonths, IsMandatory, IsActive)
            OUTPUT inserted.Id VALUES (@code, N'Corso ' + @code, N'Test', @validityMonths, @mandatory, @active)
            """, new { code, validityMonths, mandatory, active });

    public static Task<int> AddCertificateAsync(string cs, int employeeId, int courseId, DateOnly completedOn, decimal? score = null) =>
        ScalarAsync<int>(cs, """
            INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Score, Source)
            OUTPUT inserted.Id
            SELECT @employeeId, @courseId, @completedOn, ValidityMonths, @score, 'Manual' FROM dbo.Courses WHERE Id = @courseId
            """, new { employeeId, courseId, completedOn, score });
}
