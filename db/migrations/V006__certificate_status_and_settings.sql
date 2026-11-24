-- V006: usp_Certificates_Get restituisce anche lo stato (calcolato con la regola unica di V002)
-- e una procedura espone le impostazioni applicative all'API.
-- Nota: V004 e' gia' rilasciata e non si modifica; CREATE OR ALTER sostituisce la procedura.

CREATE OR ALTER PROCEDURE dbo.usp_Certificates_Get
    @Id   INT,
    @AsOf DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @AsOf = ISNULL(@AsOf, dbo.fn_BusinessToday());
    DECLARE @WarningDays INT = dbo.fn_DefaultWarningDays();

    SELECT c.Id, e.EmployeeNumber, co.Code AS CourseCode, c.CompletedOn, c.ExpiresOn, c.Score, c.Source, s.Status
    FROM dbo.Certificates AS c
    JOIN dbo.Employees AS e ON e.Id = c.EmployeeId
    JOIN dbo.Courses AS co ON co.Id = c.CourseId
    CROSS APPLY dbo.fn_CertificateStatus(c.ExpiresOn, @AsOf, @WarningDays) AS s
    WHERE c.Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_AppSettings_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT dbo.fn_DefaultWarningDays() AS ExpiringSoonDays;
END;
GO
