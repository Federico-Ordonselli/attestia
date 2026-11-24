-- V003: stored procedure di reportistica (scadenze, storico dipendente, riepilogo per reparto).
-- Tutte accettano @AsOf e @WarningDays opzionali: l'API li passa esplicitamente (data nel fuso aziendale,
-- soglia da configurazione), il portale ASP classic usa i default; i test li fissano per essere deterministici.

CREATE OR ALTER PROCEDURE dbo.usp_ExpiringCertificates
    @WithinDays  INT,
    @Department  NVARCHAR(100) = NULL,
    @Page        INT = 1,
    @PageSize    INT = 20,          -- NULL = tutte le righe (usato dall'export XML)
    @AsOf        DATE = NULL,
    @WarningDays INT = NULL,
    @TotalCount  INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @WithinDays IS NULL OR @WithinDays NOT BETWEEN 0 AND 3650
        THROW 50001, 'WithinDays deve essere compreso tra 0 e 3650.', 1;
    IF @Page IS NULL OR @Page < 1
        THROW 50002, 'Page deve essere >= 1.', 1;
    IF @PageSize IS NOT NULL AND @PageSize NOT BETWEEN 1 AND 500
        THROW 50003, 'PageSize deve essere compreso tra 1 e 500.', 1;

    SET @AsOf = ISNULL(@AsOf, dbo.fn_BusinessToday());
    SET @WarningDays = ISNULL(@WarningDays, dbo.fn_DefaultWarningDays());

    DECLARE @Horizon DATE = DATEADD(DAY, @WithinDays, @AsOf);

    SELECT m.CertificateId,
           e.EmployeeNumber,
           e.FirstName,
           e.LastName,
           e.Department,
           co.Code  AS CourseCode,
           co.Title AS CourseTitle,
           m.CompletedOn,
           m.ExpiresOn,
           DATEDIFF(DAY, @AsOf, m.ExpiresOn) AS DaysToExpiry,
           m.Status
    INTO #Rows
    FROM dbo.fn_ComplianceMatrix(@AsOf, @WarningDays) AS m
    JOIN dbo.Employees AS e ON e.Id = m.EmployeeId
    JOIN dbo.Courses AS co ON co.Id = m.CourseId
    WHERE m.ExpiresOn IS NOT NULL
      AND m.ExpiresOn <= @Horizon
      AND (@Department IS NULL OR e.Department = @Department);

    SET @TotalCount = @@ROWCOUNT;

    SELECT CertificateId, EmployeeNumber, FirstName, LastName, Department, CourseCode, CourseTitle,
           CompletedOn, ExpiresOn, DaysToExpiry, Status
    FROM #Rows
    ORDER BY ExpiresOn, LastName, FirstName, CourseCode, CertificateId
    OFFSET (@Page - 1) * ISNULL(@PageSize, 0) ROWS
    FETCH NEXT ISNULL(@PageSize, 2147483647) ROWS ONLY;
END;
GO

-- Storico completo di un dipendente: tutti gli attestati (anche quelli superati
-- da un rinnovo, con IsCurrent = 0) piu' una riga Missing per ogni corso obbligatorio mai completato.
-- E' l'unica procedura che il portale ASP classic ha il permesso di eseguire.
CREATE OR ALTER PROCEDURE dbo.usp_EmployeeCertificateHistory
    @EmployeeNumber NVARCHAR(20),
    @AsOf           DATE = NULL,
    @WarningDays    INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @AsOf = ISNULL(@AsOf, dbo.fn_BusinessToday());
    SET @WarningDays = ISNULL(@WarningDays, dbo.fn_DefaultWarningDays());

    DECLARE @EmployeeId INT = (SELECT Id FROM dbo.Employees WHERE EmployeeNumber = @EmployeeNumber);
    IF @EmployeeId IS NULL
        RETURN;

    WITH Ranked AS
    (
        SELECT c.Id, c.CourseId, c.CompletedOn, c.ExpiresOn, c.Score, c.Source,
               ROW_NUMBER() OVER (PARTITION BY c.CourseId ORDER BY c.CompletedOn DESC, c.Id DESC) AS Rn
        FROM dbo.Certificates AS c
        WHERE c.EmployeeId = @EmployeeId
    )
    SELECT e.EmployeeNumber, e.FirstName, e.LastName, e.Department,
           r.Id AS CertificateId,
           co.Code AS CourseCode, co.Title AS CourseTitle, co.Category, co.IsMandatory,
           r.CompletedOn, r.ExpiresOn, r.Score, r.Source,
           s.Status,
           CAST(CASE WHEN r.Rn = 1 THEN 1 ELSE 0 END AS BIT) AS IsCurrent
    FROM Ranked AS r
    JOIN dbo.Courses AS co ON co.Id = r.CourseId
    CROSS JOIN dbo.Employees AS e
    CROSS APPLY dbo.fn_CertificateStatus(r.ExpiresOn, @AsOf, @WarningDays) AS s
    WHERE e.Id = @EmployeeId

    UNION ALL

    SELECT e.EmployeeNumber, e.FirstName, e.LastName, e.Department,
           NULL, co.Code, co.Title, co.Category, co.IsMandatory,
           NULL, NULL, NULL, NULL,
           'Missing', CAST(1 AS BIT)
    FROM dbo.Courses AS co
    CROSS JOIN dbo.Employees AS e
    WHERE e.Id = @EmployeeId
      AND e.IsActive = 1
      AND co.IsActive = 1
      AND co.IsMandatory = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.Certificates AS c WHERE c.EmployeeId = @EmployeeId AND c.CourseId = co.Id)

    ORDER BY CourseTitle, CompletedOn DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_DepartmentSummary
    @AsOf        DATE = NULL,
    @WarningDays INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @AsOf = ISNULL(@AsOf, dbo.fn_BusinessToday());
    SET @WarningDays = ISNULL(@WarningDays, dbo.fn_DefaultWarningDays());

    WITH Counts AS
    (
        SELECT m.EmployeeId, m.Status FROM dbo.fn_ComplianceMatrix(@AsOf, @WarningDays) AS m
    )
    SELECT e.Department,
           COUNT(DISTINCT e.Id)                                         AS EmployeeCount,
           COUNT(CASE WHEN c.Status = 'Valid' THEN 1 END)               AS ValidCount,
           COUNT(CASE WHEN c.Status = 'ExpiringSoon' THEN 1 END)        AS ExpiringSoonCount,
           COUNT(CASE WHEN c.Status = 'Expired' THEN 1 END)             AS ExpiredCount,
           COUNT(CASE WHEN c.Status = 'Missing' THEN 1 END)             AS MissingCount
    FROM dbo.Employees AS e
    LEFT JOIN Counts AS c ON c.EmployeeId = e.Id
    WHERE e.IsActive = 1
    GROUP BY e.Department
    ORDER BY e.Department;
END;
GO
