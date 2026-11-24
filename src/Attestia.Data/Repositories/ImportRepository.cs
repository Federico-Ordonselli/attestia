using System.Data;
using Attestia.Core.Errors;
using Attestia.Core.Importing;
using Attestia.Core.Xml;
using Attestia.Data.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Attestia.Data.Repositories;

public sealed record ImportLogEntry
{
    public int Id { get; init; }
    public string FileName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public DateTime ImportedAt { get; init; }
    public string ImportedBy { get; init; } = "";
    public int TotalRows { get; init; }
    public int OkRows { get; init; }
    public int RejectedRows { get; init; }
    public string? ErrorsJson { get; init; }
}

public sealed record ImportSaveRequest(string FileName, string Sha256, string ImportedBy, int TotalRows, string? ErrorsJson, IReadOnlyList<ImportRowResult> Accepted);

public interface IImportRepository
{
    Task<ImportLogEntry?> GetBySha256Async(string sha256, CancellationToken ct);
    Task<ImportReferenceData> PrepareAsync(IReadOnlyList<ParsedCompletion> rows, CancellationToken ct);
    Task<ImportLogEntry> SaveAsync(ImportSaveRequest request, CancellationToken ct);
    Task<IReadOnlyList<ImportLogEntry>> ListAsync(int top, CancellationToken ct);
}

/// <summary>Eccezione transitoria: i dati di riferimento sono cambiati durante l'import (riprovare la validazione).</summary>
public sealed class ImportRaceException(string message) : Exception(message);

public sealed class ImportRepository(ISqlConnectionFactory db) : IImportRepository
{
    private const string RowType = "dbo.CertificateImportRowType";

    public async Task<ImportLogEntry?> GetBySha256Async(string sha256, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<ImportLogEntry>(Proc.Cmd("dbo.usp_Imports_GetBySha256", new { Sha256 = sha256 }, ct));
    }

    public async Task<ImportReferenceData> PrepareAsync(IReadOnlyList<ParsedCompletion> rows, CancellationToken ct)
    {
        var table = ToTable(rows.Select(r => (r.LineNumber, r.EmployeeNumber, r.CourseCode, r.CompletedOn, r.Score)));
        await using var c = db.Create();
        await using var grid = await c.QueryMultipleAsync(Proc.Cmd("dbo.usp_Imports_Prepare", new { Rows = table.AsTableValuedParameter(RowType) }, ct));

        var employees = (await grid.ReadAsync<EmployeeRef>()).ToDictionary(e => e.EmployeeNumber, StringComparer.Ordinal);
        var courses = (await grid.ReadAsync<CourseRef>()).ToDictionary(e => e.Code, StringComparer.Ordinal);
        var existing = (await grid.ReadAsync<CertificateKey>()).ToHashSet();
        return new ImportReferenceData(employees, courses, existing);
    }

    public async Task<ImportLogEntry> SaveAsync(ImportSaveRequest request, CancellationToken ct)
    {
        var table = ToTable(request.Accepted.Select(r => (r.LineNumber, r.EmployeeNumber, r.CourseCode, r.CompletedOn, r.Score)));
        try
        {
            await using var c = db.Create();
            return await c.QuerySingleAsync<ImportLogEntry>(Proc.Cmd("dbo.usp_Imports_Save", new
            {
                request.FileName,
                request.Sha256,
                request.ImportedBy,
                request.TotalRows,
                request.ErrorsJson,
                Rows = table.AsTableValuedParameter(RowType),
            }, ct));
        }
        catch (SqlException ex) when (SqlErrors.IsUniqueViolation(ex) && ex.Message.Contains("UQ_ImportLogs_Sha256", StringComparison.Ordinal))
        {
            throw new ConflictException("Questo file è già stato importato.");
        }
        catch (SqlException ex) when (SqlErrors.IsUniqueViolation(ex) || ex.Number == SqlErrors.ImportReferenceChanged)
        {
            throw new ImportRaceException("Dati cambiati durante l'import.");
        }
    }

    public async Task<IReadOnlyList<ImportLogEntry>> ListAsync(int top, CancellationToken ct)
    {
        await using var c = db.Create();
        return (await c.QueryAsync<ImportLogEntry>(Proc.Cmd("dbo.usp_Imports_List", new { Top = top }, ct))).AsList();
    }

    private static DataTable ToTable(IEnumerable<(int Line, string Employee, string Course, DateOnly Completed, decimal? Score)> rows)
    {
#pragma warning disable CA2000 // la DataTable vive quanto la chiamata ed e' passata come TVP
        var table = new DataTable();
#pragma warning restore CA2000
        table.Locale = System.Globalization.CultureInfo.InvariantCulture;
        table.Columns.Add("RowNumber", typeof(int));
        table.Columns.Add("LineNumber", typeof(int));
        table.Columns.Add("EmployeeNumber", typeof(string));
        table.Columns.Add("CourseCode", typeof(string));
        table.Columns.Add("CompletedOn", typeof(DateTime));
        table.Columns.Add("Score", typeof(decimal));
        var rowNumber = 0;
        foreach (var r in rows)
        {
            table.Rows.Add(++rowNumber, r.Line, r.Employee, r.Course, r.Completed.ToDateTime(TimeOnly.MinValue), (object?)r.Score ?? DBNull.Value);
        }

        return table;
    }
}
