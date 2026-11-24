-- V002: regole di stato degli attestati, definite UNA volta sola in T-SQL.
-- Le stesse regole sono replicate in Attestia.Core (CertificateStatusRules) e un test di parita'
-- esegue lo stesso insieme di casi su entrambe le implementazioni.
--
-- Regole (AsOf = data di riferimento, WarningDays = soglia "in scadenza"):
--   ExpiresOn IS NULL                          -> Valid        (il corso non scade)
--   ExpiresOn <  AsOf                          -> Expired      (ExpiresOn e' l'ultimo giorno di validita')
--   ExpiresOn <= AsOf + WarningDays            -> ExpiringSoon
--   altrimenti                                 -> Valid
--   corso obbligatorio senza alcun attestato   -> Missing

CREATE FUNCTION dbo.fn_CertificateStatus (@ExpiresOn DATE, @AsOf DATE, @WarningDays INT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT CAST(CASE
                    WHEN @ExpiresOn IS NULL THEN 'Valid'
                    WHEN @ExpiresOn < @AsOf THEN 'Expired'
                    WHEN @ExpiresOn <= DATEADD(DAY, @WarningDays, @AsOf) THEN 'ExpiringSoon'
                    ELSE 'Valid'
                END AS VARCHAR(12)) AS Status;
GO

-- Soglia di default letta da AppSettings (fallback 60 se la riga manca o non e' numerica).
CREATE FUNCTION dbo.fn_DefaultWarningDays ()
RETURNS INT
WITH SCHEMABINDING
AS
BEGIN
    RETURN ISNULL((SELECT TRY_CAST([Value] AS INT) FROM dbo.AppSettings WHERE [Key] = 'ExpiringSoonDays'), 60);
END;
GO

-- "Oggi" nel fuso aziendale (Europa/Roma), non in quello del server (UTC nel container).
CREATE FUNCTION dbo.fn_BusinessToday ()
RETURNS DATE
AS
BEGIN
    RETURN CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'W. Europe Standard Time' AS DATE);
END;
GO

-- Matrice di conformita': per ogni dipendente attivo e corso attivo, l'attestato piu' recente
-- (se esiste) con il suo stato; i corsi obbligatori senza attestato compaiono come Missing.
-- E' la base comune di scadenze e riepilogo per reparto.
CREATE FUNCTION dbo.fn_ComplianceMatrix (@AsOf DATE, @WarningDays INT)
RETURNS TABLE
AS
RETURN
    SELECT e.Id          AS EmployeeId,
           co.Id         AS CourseId,
           lc.Id         AS CertificateId,
           lc.CompletedOn,
           lc.ExpiresOn,
           lc.Score,
           lc.Source,
           CAST(CASE WHEN lc.Id IS NULL THEN 'Missing' ELSE s.Status END AS VARCHAR(12)) AS Status
    FROM dbo.Employees AS e
    CROSS JOIN dbo.Courses AS co
    OUTER APPLY (SELECT TOP (1) c.Id, c.CompletedOn, c.ExpiresOn, c.Score, c.Source
                 FROM dbo.Certificates AS c
                 WHERE c.EmployeeId = e.Id AND c.CourseId = co.Id
                 ORDER BY c.CompletedOn DESC, c.Id DESC) AS lc
    OUTER APPLY dbo.fn_CertificateStatus(lc.ExpiresOn, @AsOf, @WarningDays) AS s
    WHERE e.IsActive = 1
      AND co.IsActive = 1
      AND (lc.Id IS NOT NULL OR co.IsMandatory = 1);
GO
