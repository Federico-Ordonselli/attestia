using Attestia.Core.Certificates;
using Attestia.Core.Importing;
using Attestia.Core.Xml;

namespace Attestia.Tests.Xml;

public sealed class ImportValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    private static readonly ImportReferenceData Refs = new(
        new Dictionary<string, EmployeeRef>
        {
            ["E1"] = new("E1", true),
            ["E2"] = new("E2", true),
            ["OLD"] = new("OLD", false),
        },
        new Dictionary<string, CourseRef>
        {
            ["GDPR"] = new("GDPR", true, 24),
            ["GEN"] = new("GEN", true, null),
            ["RETIRED"] = new("RETIRED", false, 12),
        },
        new HashSet<CertificateKey> { new("E2", "GDPR", new DateOnly(2026, 1, 10)) });

    private static ParsedCompletion Row(int line, string emp, string course, string date) =>
        new(line, emp, course, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), null);

    [Theory]
    [InlineData("NOPE", "GDPR", "2026-01-01", "Matricola inesistente.")]
    [InlineData("OLD", "GDPR", "2026-01-01", "Dipendente non più attivo.")]
    [InlineData("E1", "NOPE", "2026-01-01", "Codice corso inesistente.")]
    [InlineData("E1", "RETIRED", "2026-01-01", "Corso disattivato.")]
    [InlineData("E1", "GDPR", "2026-11-25", "Data di completamento futura.")]
    [InlineData("E1", "GDPR", "1999-12-31", "Data di completamento precedente al 2000.")]
    [InlineData("E2", "GDPR", "2026-01-10", "Attestato già registrato.")]
    public void Each_rule_rejects_the_row_with_a_reason(string emp, string course, string date, string reason)
    {
        var plan = ImportValidator.Validate([Row(7, emp, course, date)], Refs, Today, 60);

        var result = Assert.Single(plan.Rows);
        Assert.Equal(ImportRowOutcome.Rejected, result.Outcome);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(7, result.LineNumber);
        Assert.Null(result.Status);
    }

    [Fact]
    public void Today_is_accepted_and_status_is_computed_with_the_shared_rules()
    {
        var plan = ImportValidator.Validate([Row(3, "E1", "GDPR", "2026-11-24"), Row(9, "E1", "GEN", "2010-05-05"), Row(12, "E2", "GDPR", "2024-12-01")], Refs, Today, 60);

        Assert.All(plan.Rows, r => Assert.Equal(ImportRowOutcome.Imported, r.Outcome));
        Assert.Equal(new DateOnly(2028, 11, 24), plan.Rows[0].ExpiresOn);
        Assert.Equal(CertificateStatus.Valid, plan.Rows[0].Status);
        Assert.Null(plan.Rows[1].ExpiresOn);
        Assert.Equal(CertificateStatus.Valid, plan.Rows[1].Status);
        Assert.Equal(CertificateStatus.ExpiringSoon, plan.Rows[2].Status); // scade il 01/12/2026, tra 7 giorni
    }

    [Fact]
    public void Duplicate_rows_in_the_same_file_point_to_the_first_occurrence()
    {
        var plan = ImportValidator.Validate([Row(4, "E1", "GDPR", "2026-03-01"), Row(10, "E1", "GDPR", "2026-03-01"), Row(16, "E1", "GDPR", "2026-03-01")], Refs, Today, 60);

        Assert.Equal(ImportRowOutcome.Imported, plan.Rows[0].Outcome);
        Assert.Equal("Riga duplicata nel file (stessi dati della riga 4).", plan.Rows[1].Reason);
        Assert.Equal("Riga duplicata nel file (stessi dati della riga 4).", plan.Rows[2].Reason);
        Assert.Single(plan.Accepted);
        Assert.Equal(2, plan.Rejected.Count());
    }
}
