using Attestia.Core.Certificates;

namespace Attestia.Core.Models;

// Modelli di lettura condivisi tra Data (materializzati da Dapper, i nomi coincidono con le colonne delle
// stored procedure) e Api (serializzati in JSON). Proprieta' init-only: Dapper le valorizza per nome.

public sealed record Course
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public int? ValidityMonths { get; init; }
    public bool IsMandatory { get; init; }
    public bool IsActive { get; init; }
}

public sealed record Employee
{
    public int Id { get; init; }
    public string EmployeeNumber { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Department { get; init; } = "";
    public string Role { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed record CertificateRecord
{
    public int Id { get; init; }
    public string EmployeeNumber { get; init; } = "";
    public string CourseCode { get; init; } = "";
    public DateOnly CompletedOn { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public decimal? Score { get; init; }
    public string Source { get; init; } = "";
    public CertificateStatus Status { get; init; }
}

public sealed record CertificateHistoryEntry
{
    public int? CertificateId { get; init; }
    public string CourseCode { get; init; } = "";
    public string CourseTitle { get; init; } = "";
    public string Category { get; init; } = "";
    public bool IsMandatory { get; init; }
    public DateOnly? CompletedOn { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public decimal? Score { get; init; }
    public string? Source { get; init; }
    public CertificateStatus Status { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed record EmployeeHistory
{
    public required Employee Employee { get; init; }
    public required IReadOnlyList<CertificateHistoryEntry> Certificates { get; init; }
}

public sealed record ExpiringCertificate
{
    public int CertificateId { get; init; }
    public string EmployeeNumber { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Department { get; init; } = "";
    public string CourseCode { get; init; } = "";
    public string CourseTitle { get; init; } = "";
    public DateOnly CompletedOn { get; init; }
    public DateOnly ExpiresOn { get; init; }
    public int DaysToExpiry { get; init; }
    public CertificateStatus Status { get; init; }
}

public sealed record DepartmentSummary
{
    public string Department { get; init; } = "";
    public int EmployeeCount { get; init; }
    public int ValidCount { get; init; }
    public int ExpiringSoonCount { get; init; }
    public int ExpiredCount { get; init; }
    public int MissingCount { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
