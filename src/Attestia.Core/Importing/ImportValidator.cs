using Attestia.Core.Certificates;
using Attestia.Core.Xml;

namespace Attestia.Core.Importing;

public sealed record EmployeeRef(string EmployeeNumber, bool IsActive);

public sealed record CourseRef(string Code, bool IsActive, int? ValidityMonths);

public sealed record CertificateKey(string EmployeeNumber, string CourseCode, DateOnly CompletedOn);

/// <summary>Dati di riferimento per le sole matricole/corsi presenti nel file (vedi usp_Imports_Prepare).</summary>
public sealed record ImportReferenceData(
    IReadOnlyDictionary<string, EmployeeRef> Employees,
    IReadOnlyDictionary<string, CourseRef> Courses,
    IReadOnlySet<CertificateKey> ExistingCertificates);

public enum ImportRowOutcome
{
    Imported,
    Rejected,
}

/// <summary>Esito di una riga del file, mostrato nel report e (per le scartate) salvato in ImportLogs.ErrorsJson.</summary>
public sealed record ImportRowResult
{
    public required int LineNumber { get; init; }
    public required string EmployeeNumber { get; init; }
    public required string CourseCode { get; init; }
    public required DateOnly CompletedOn { get; init; }
    public decimal? Score { get; init; }
    public required ImportRowOutcome Outcome { get; init; }
    public string? Reason { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public CertificateStatus? Status { get; init; }
}

public sealed record ImportPlan(IReadOnlyList<ImportRowResult> Rows)
{
    public IEnumerable<ImportRowResult> Accepted => Rows.Where(r => r.Outcome == ImportRowOutcome.Imported);

    public IEnumerable<ImportRowResult> Rejected => Rows.Where(r => r.Outcome == ImportRowOutcome.Rejected);
}

/// <summary>
/// Regole di dominio per riga, applicate DOPO la validazione XSD. Una riga e' scartata (con motivo) se:
/// la matricola non esiste o non e' attiva, il corso non esiste o e' disattivato, la data e' futura o
/// anteriore al 2000, duplica una riga precedente dello stesso file, o l'attestato esiste gia'.
/// Le righe accettate riportano scadenza e stato calcolati con <see cref="CertificateStatusRules"/>.
/// </summary>
public static class ImportValidator
{
    private static readonly DateOnly MinDate = new(2000, 1, 1);

    public static ImportPlan Validate(IReadOnlyList<ParsedCompletion> rows, ImportReferenceData refs, DateOnly today, int warningDays)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(refs);

        var firstSeen = new Dictionary<CertificateKey, int>();
        var results = new List<ImportRowResult>(rows.Count);

        foreach (var row in rows)
        {
            var key = new CertificateKey(row.EmployeeNumber, row.CourseCode, row.CompletedOn);
            var duplicateOf = firstSeen.TryGetValue(key, out var line) ? line : (int?)null;
            firstSeen.TryAdd(key, row.LineNumber);

            var reason = RejectionReason(row, key, duplicateOf, refs, today);
            var course = refs.Courses.GetValueOrDefault(row.CourseCode);
            var expiresOn = reason is null && course is not null ? CertificateStatusRules.ExpiresOn(row.CompletedOn, course.ValidityMonths) : null;

            results.Add(new ImportRowResult
            {
                LineNumber = row.LineNumber,
                EmployeeNumber = row.EmployeeNumber,
                CourseCode = row.CourseCode,
                CompletedOn = row.CompletedOn,
                Score = row.Score,
                Outcome = reason is null ? ImportRowOutcome.Imported : ImportRowOutcome.Rejected,
                Reason = reason,
                ExpiresOn = expiresOn,
                Status = reason is null ? CertificateStatusRules.Evaluate(expiresOn, today, warningDays) : null,
            });
        }

        return new ImportPlan(results);
    }

    private static string? RejectionReason(ParsedCompletion row, CertificateKey key, int? duplicateOf, ImportReferenceData refs, DateOnly today)
    {
        if (!refs.Employees.TryGetValue(row.EmployeeNumber, out var employee))
        {
            return "Matricola inesistente.";
        }

        if (!employee.IsActive)
        {
            return "Dipendente non più attivo.";
        }

        if (!refs.Courses.TryGetValue(row.CourseCode, out var course))
        {
            return "Codice corso inesistente.";
        }

        if (!course.IsActive)
        {
            return "Corso disattivato.";
        }

        if (row.CompletedOn > today)
        {
            return "Data di completamento futura.";
        }

        if (row.CompletedOn < MinDate)
        {
            return "Data di completamento precedente al 2000.";
        }

        if (duplicateOf is not null)
        {
            return $"Riga duplicata nel file (stessi dati della riga {duplicateOf}).";
        }

        return refs.ExistingCertificates.Contains(key) ? "Attestato già registrato." : null;
    }
}
