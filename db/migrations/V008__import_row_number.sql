-- V008: il table type dell'import usa un progressivo (RowNumber) come chiave al posto del numero di riga.
-- Un file XML scritto su una sola riga ha piu' <completion> con lo stesso LineNumber: la chiave primaria
-- su LineNumber faceva fallire l'import (scoperto da un test di integrazione).
-- Un table type non si puo' modificare: si eliminano le procedure che lo usano, si ricrea il tipo,
-- si ricreano le procedure. Tutto nello stesso script (DbUp: una transazione per script).

DROP PROCEDURE IF EXISTS dbo.usp_Imports_Prepare;
DROP PROCEDURE IF EXISTS dbo.usp_Imports_Save;
DROP TYPE IF EXISTS dbo.CertificateImportRowType;
GO

CREATE TYPE dbo.CertificateImportRowType AS TABLE
(
    RowNumber      INT           NOT NULL PRIMARY KEY, -- posizione della <completion> nel file (1, 2, ...)
    LineNumber     INT           NOT NULL,             -- riga del file, per i messaggi
    EmployeeNumber NVARCHAR(20)  NOT NULL,
    CourseCode     VARCHAR(20)   NOT NULL,
    CompletedOn    DATE          NOT NULL,
    Score          DECIMAL(5, 2) NULL
);
GO

GRANT EXECUTE ON TYPE::dbo.CertificateImportRowType TO attestia_api;
GO

CREATE PROCEDURE dbo.usp_Imports_Prepare
    @Rows dbo.CertificateImportRowType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EmployeeNumber, e.IsActive
    FROM dbo.Employees AS e
    WHERE e.EmployeeNumber IN (SELECT EmployeeNumber FROM @Rows);

    SELECT co.Code, co.IsActive, CAST(co.ValidityMonths AS INT) AS ValidityMonths
    FROM dbo.Courses AS co
    WHERE co.Code IN (SELECT CourseCode FROM @Rows);

    SELECT DISTINCT r.EmployeeNumber, r.CourseCode, r.CompletedOn
    FROM @Rows AS r
    JOIN dbo.Employees AS e ON e.EmployeeNumber = r.EmployeeNumber
    JOIN dbo.Courses AS co ON co.Code = r.CourseCode
    JOIN dbo.Certificates AS c ON c.EmployeeId = e.Id AND c.CourseId = co.Id AND c.CompletedOn = r.CompletedOn;
END;
GO

CREATE PROCEDURE dbo.usp_Imports_Save
    @FileName     NVARCHAR(260),
    @Sha256       CHAR(64),
    @ImportedBy   NVARCHAR(20),
    @TotalRows    INT,
    @ErrorsJson   NVARCHAR(MAX) = NULL,
    @Rows         dbo.CertificateImportRowType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Ok INT = (SELECT COUNT(*) FROM @Rows);

    BEGIN TRANSACTION;

    DECLARE @Log TABLE (Id INT);
    INSERT INTO dbo.ImportLogs (FileName, Sha256, ImportedBy, TotalRows, OkRows, RejectedRows, ErrorsJson)
    OUTPUT inserted.Id INTO @Log
    VALUES (@FileName, @Sha256, @ImportedBy, @TotalRows, @Ok, @TotalRows - @Ok, @ErrorsJson);

    DECLARE @ImportLogId INT = (SELECT Id FROM @Log);

    INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Score, Source, ImportLogId)
    SELECT e.Id, co.Id, r.CompletedOn, co.ValidityMonths, r.Score, 'XmlImport', @ImportLogId
    FROM @Rows AS r
    JOIN dbo.Employees AS e ON e.EmployeeNumber = r.EmployeeNumber AND e.IsActive = 1
    JOIN dbo.Courses AS co ON co.Code = r.CourseCode AND co.IsActive = 1;

    IF @@ROWCOUNT <> @Ok
        THROW 51004, 'Dati di riferimento cambiati durante l''import: ripetere la validazione.', 1;

    COMMIT TRANSACTION;

    SELECT Id, FileName, Sha256, ImportedAt, ImportedBy, TotalRows, OkRows, RejectedRows
    FROM dbo.ImportLogs WHERE Id = @ImportLogId;
END;
GO
