using System.ComponentModel.DataAnnotations;

namespace Attestia.Api.Endpoints;

// Richieste in ingresso. La validazione e' quella integrata delle minimal API di .NET 10 (AddValidation):
// gli errori diventano automaticamente 400 HttpValidationProblemDetails.

public sealed record CreateCourseRequest
{
    [Required, RegularExpression("^[A-Z0-9-]{2,20}$", ErrorMessage = "Il codice deve avere 2-20 caratteri tra A-Z, 0-9 e '-'.")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; init; } = "";

    [Required, StringLength(100, MinimumLength = 2)]
    public string Category { get; init; } = "";

    /// <summary>Mesi di validita'; null = l'attestato non scade.</summary>
    [Range(1, 240)]
    public int? ValidityMonths { get; init; }

    public bool IsMandatory { get; init; }
}

public sealed record UpdateCourseRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; init; } = "";

    [Required, StringLength(100, MinimumLength = 2)]
    public string Category { get; init; } = "";

    [Range(1, 240)]
    public int? ValidityMonths { get; init; }

    public bool IsMandatory { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record CreateCertificateRequest
{
    [Required, RegularExpression("^[A-Z0-9-]{2,20}$")]
    public string EmployeeNumber { get; init; } = "";

    [Required, RegularExpression("^[A-Z0-9-]{2,20}$")]
    public string CourseCode { get; init; } = "";

    [Required]
    public DateOnly CompletedOn { get; init; }

    [Range(0, 100)]
    public decimal? Score { get; init; }
}

public sealed record UpdateCertificateRequest
{
    [Required]
    public DateOnly CompletedOn { get; init; }

    [Range(0, 100)]
    public decimal? Score { get; init; }
}

public sealed record MeResponse(string EmployeeNumber, string Role, string DisplayName, string? Department);
