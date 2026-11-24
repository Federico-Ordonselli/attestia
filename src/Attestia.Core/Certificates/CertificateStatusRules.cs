namespace Attestia.Core.Certificates;

/// <summary>
/// Replica C# di <c>dbo.fn_CertificateStatus</c> e del calcolo di <c>Certificates.ExpiresOn</c>.
/// Qualsiasi modifica va fatta in entrambe le implementazioni: il test di parita' esegue gli stessi casi su SQL Server.
/// </summary>
public static class CertificateStatusRules
{
    public const int DefaultWarningDays = 60;

    /// <summary>
    /// Equivalente di <c>DATEADD(MONTH, n, CompletedOn)</c>: se il giorno non esiste nel mese di arrivo
    /// si usa l'ultimo giorno del mese (31/01 + 1 mese = 28 o 29/02), come <see cref="DateOnly.AddMonths"/>.
    /// </summary>
    public static DateOnly? ExpiresOn(DateOnly completedOn, int? validityMonths) =>
        validityMonths is null ? null : completedOn.AddMonths(validityMonths.Value);

    /// <summary><paramref name="expiresOn"/> e' l'ultimo giorno di validita' (incluso).</summary>
    public static CertificateStatus Evaluate(DateOnly? expiresOn, DateOnly asOf, int warningDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warningDays);

        if (expiresOn is null)
        {
            return CertificateStatus.Valid;
        }

        if (expiresOn.Value < asOf)
        {
            return CertificateStatus.Expired;
        }

        return expiresOn.Value <= asOf.AddDays(warningDays) ? CertificateStatus.ExpiringSoon : CertificateStatus.Valid;
    }

    /// <summary>Stato "di conformita'" di un dipendente per un corso, dato l'attestato piu' recente (se esiste).</summary>
    public static CertificateStatus? EvaluateCompliance(bool hasCertificate, DateOnly? latestExpiresOn, bool courseIsMandatory, DateOnly asOf, int warningDays)
    {
        if (!hasCertificate)
        {
            return courseIsMandatory ? CertificateStatus.Missing : null;
        }

        return Evaluate(latestExpiresOn, asOf, warningDays);
    }
}
