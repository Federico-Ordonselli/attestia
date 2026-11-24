using Attestia.Core.Certificates;

namespace Attestia.Tests.Domain;

public sealed class CertificateStatusRulesTests
{
    [Theory]
    [MemberData(nameof(StatusCases.All), MemberType = typeof(StatusCases))]
    public void Evaluate_follows_the_documented_rules(DateOnly? expiresOn, DateOnly asOf, int warningDays, CertificateStatus expected) =>
        Assert.Equal(expected, CertificateStatusRules.Evaluate(expiresOn, asOf, warningDays));

    [Theory]
    [MemberData(nameof(StatusCases.Expiries), MemberType = typeof(StatusCases))]
    public void ExpiresOn_matches_dateadd_month_semantics(DateOnly completedOn, int? months, DateOnly? expected) =>
        Assert.Equal(expected, CertificateStatusRules.ExpiresOn(completedOn, months));

    [Fact]
    public void Negative_warning_days_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CertificateStatusRules.Evaluate(null, new DateOnly(2026, 1, 1), -1));

    [Theory]
    [InlineData(false, true, CertificateStatus.Missing)]
    [InlineData(false, false, null)]
    public void Compliance_without_certificate_depends_on_mandatory_flag(bool hasCertificate, bool mandatory, CertificateStatus? expected) =>
        Assert.Equal(expected, CertificateStatusRules.EvaluateCompliance(hasCertificate, null, mandatory, new DateOnly(2026, 1, 1), 60));

    [Fact]
    public void Compliance_with_certificate_uses_the_status_rules() =>
        Assert.Equal(CertificateStatus.Expired,
            CertificateStatusRules.EvaluateCompliance(true, new DateOnly(2025, 12, 31), courseIsMandatory: true, new DateOnly(2026, 1, 1), 60));
}
