using Attestia.Core.Certificates;

namespace Attestia.Tests.Domain;

/// <summary>
/// UNICO insieme di casi per le regole di stato. Lo usano sia il test unitario C# sia il test di parita'
/// che esegue gli stessi casi su <c>dbo.fn_CertificateStatus</c> in SQL Server.
/// </summary>
public static class StatusCases
{
    private static readonly DateOnly AsOf = new(2026, 6, 15);

    public static TheoryData<DateOnly?, DateOnly, int, CertificateStatus> All => new()
    {
        { null, AsOf, 60, CertificateStatus.Valid },                       // corso che non scade
        { AsOf.AddDays(-1), AsOf, 60, CertificateStatus.Expired },          // scaduto ieri
        { AsOf.AddYears(-3), AsOf, 60, CertificateStatus.Expired },
        { AsOf, AsOf, 60, CertificateStatus.ExpiringSoon },                 // ultimo giorno di validita'
        { AsOf.AddDays(1), AsOf, 60, CertificateStatus.ExpiringSoon },
        { AsOf.AddDays(60), AsOf, 60, CertificateStatus.ExpiringSoon },     // limite della soglia, incluso
        { AsOf.AddDays(61), AsOf, 60, CertificateStatus.Valid },
        { AsOf.AddDays(10), AsOf, 0, CertificateStatus.Valid },             // soglia zero
        { AsOf, AsOf, 0, CertificateStatus.ExpiringSoon },
        { AsOf.AddDays(29), AsOf, 30, CertificateStatus.ExpiringSoon },
        { AsOf.AddDays(31), AsOf, 30, CertificateStatus.Valid },
        { new DateOnly(2028, 2, 29), new DateOnly(2027, 12, 31), 60, CertificateStatus.ExpiringSoon }, // 60 gg esatti, anno bisestile
    };

    public static TheoryData<DateOnly, int?, DateOnly?> Expiries => new()
    {
        { new DateOnly(2024, 3, 10), 12, new DateOnly(2025, 3, 10) },
        { new DateOnly(2024, 1, 31), 1, new DateOnly(2024, 2, 29) },
        { new DateOnly(2023, 1, 31), 1, new DateOnly(2023, 2, 28) },
        { new DateOnly(2024, 2, 29), 12, new DateOnly(2025, 2, 28) },
        { new DateOnly(2024, 8, 31), 6, new DateOnly(2025, 2, 28) },
        { new DateOnly(2020, 5, 5), 240, new DateOnly(2040, 5, 5) },
        { new DateOnly(2020, 5, 5), null, null },
    };
}
