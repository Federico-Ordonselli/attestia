using Attestia.Core.Certificates;
using Attestia.Tests.Domain;
using Attestia.Tests.Infrastructure;

namespace Attestia.Tests.Database;

/// <summary>
/// Parita' tra la regola T-SQL (fonte di verita' per i report) e la replica C#, sugli STESSI casi.
/// Se questo test fallisce, una delle due implementazioni e' stata modificata senza l'altra.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StatusParityTests(ClassDatabase database) : IClassFixture<ClassDatabase>
{
    private readonly string _cs = database.ConnectionString;

    [Theory]
    [MemberData(nameof(StatusCases.All), MemberType = typeof(StatusCases))]
    public async Task Sql_status_function_matches_csharp(DateOnly? expiresOn, DateOnly asOf, int warningDays, CertificateStatus expected)
    {
        var sql = await Sql.ScalarAsync<string>(_cs, "SELECT Status FROM dbo.fn_CertificateStatus(@expiresOn, @asOf, @warningDays)",
            new { expiresOn = expiresOn?.ToDateTime(TimeOnly.MinValue), asOf = asOf.ToDateTime(TimeOnly.MinValue), warningDays });

        Assert.Equal(expected, Enum.Parse<CertificateStatus>(sql));
        Assert.Equal(CertificateStatusRules.Evaluate(expiresOn, asOf, warningDays), Enum.Parse<CertificateStatus>(sql));
    }

    [Theory]
    [MemberData(nameof(StatusCases.Expiries), MemberType = typeof(StatusCases))]
    public async Task Sql_expiry_computation_matches_csharp(DateOnly completedOn, int? months, DateOnly? expected)
    {
        // Inserimento reale: si legge la colonna calcolata persistita Certificates.ExpiresOn.
        var key = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var employee = await Sql.AddEmployeeAsync(_cs, $"P{key}");
        var course = await Sql.AddCourseAsync(_cs, $"P{key}", months);
        var id = await Sql.AddCertificateAsync(_cs, employee, course, completedOn);
        var sql = await Sql.QueryAsync<DateTime?>(_cs, "SELECT ExpiresOn FROM dbo.Certificates WHERE Id = @id", new { id });

        var fromSql = sql[0] is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        Assert.Equal(expected, fromSql);
        Assert.Equal(CertificateStatusRules.ExpiresOn(completedOn, months), fromSql);
    }

    [Fact]
    public async Task Default_warning_days_come_from_app_settings()
    {
        Assert.Equal(CertificateStatusRules.DefaultWarningDays, await Sql.ScalarAsync<int>(_cs, "SELECT dbo.fn_DefaultWarningDays()"));
    }
}
