-- V004: stored procedure per anagrafiche, attestati e import.
-- Codici di errore applicativi (THROW 51xxx) tradotti dall'API in ProblemDetails:
--   51001 corso con lo stesso codice gia' esistente      -> 409
--   51002 dipendente inesistente                         -> 422
--   51003 corso inesistente o disattivato                -> 422
--   2627  violazione di unicita' (attestato duplicato)   -> 409

---------------------------------------------------------------------------------------------------
-- Corsi
---------------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_Courses_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Code, Title, Category, ValidityMonths, IsMandatory, IsActive
    FROM dbo.Courses
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY Title;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Courses_Get
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Code, Title, Category, ValidityMonths, IsMandatory, IsActive
    FROM dbo.Courses
    WHERE Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Courses_Insert
    @Code           VARCHAR(20),
    @Title          NVARCHAR(200),
    @Category       NVARCHAR(100),
    @ValidityMonths SMALLINT = NULL,
    @IsMandatory    BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.Courses WITH (UPDLOCK, HOLDLOCK) WHERE Code = @Code)
        THROW 51001, 'Esiste gia'' un corso con questo codice.', 1;

    INSERT INTO dbo.Courses (Code, Title, Category, ValidityMonths, IsMandatory, IsActive)
    OUTPUT inserted.Id, inserted.Code, inserted.Title, inserted.Category, inserted.ValidityMonths,
           inserted.IsMandatory, inserted.IsActive
    VALUES (@Code, @Title, @Category, @ValidityMonths, @IsMandatory, 1);
END;
GO

-- Il codice non si modifica: e' la chiave usata dai file di import dell'LMS.
CREATE OR ALTER PROCEDURE dbo.usp_Courses_Update
    @Id             INT,
    @Title          NVARCHAR(200),
    @Category       NVARCHAR(100),
    @ValidityMonths SMALLINT = NULL,
    @IsMandatory    BIT,
    @IsActive       BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Courses
    SET Title = @Title, Category = @Category, ValidityMonths = @ValidityMonths,
        IsMandatory = @IsMandatory, IsActive = @IsActive
    OUTPUT inserted.Id, inserted.Code, inserted.Title, inserted.Category, inserted.ValidityMonths,
           inserted.IsMandatory, inserted.IsActive
    WHERE Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Courses_Deactivate
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Courses SET IsActive = 0 WHERE Id = @Id;
    SELECT @@ROWCOUNT AS Affected;
END;
GO

---------------------------------------------------------------------------------------------------
-- Dipendenti
---------------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_Employees_Search
    @Query      NVARCHAR(100) = NULL,
    @Department NVARCHAR(100) = NULL,
    @Top        INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    -- I caratteri jolly di LIKE inseriti dall'utente vengono neutralizzati.
    DECLARE @Pattern NVARCHAR(210) =
        CASE WHEN NULLIF(LTRIM(RTRIM(@Query)), N'') IS NULL THEN NULL
             ELSE N'%' + REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Query)), N'\', N'\\'), N'%', N'\%'), N'_', N'\_'), N'[', N'\[') + N'%'
        END;

    SELECT TOP (@Top) Id, EmployeeNumber, FirstName, LastName, Email, Department, Role, IsActive
    FROM dbo.Employees
    WHERE (@Department IS NULL OR Department = @Department)
      AND (@Pattern IS NULL
           OR EmployeeNumber LIKE @Pattern ESCAPE N'\'
           OR LastName LIKE @Pattern ESCAPE N'\'
           OR FirstName LIKE @Pattern ESCAPE N'\'
           OR (FirstName + N' ' + LastName) LIKE @Pattern ESCAPE N'\')
    ORDER BY LastName, FirstName, EmployeeNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Employees_GetByNumber
    @EmployeeNumber NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, EmployeeNumber, FirstName, LastName, Email, Department, Role, IsActive
    FROM dbo.Employees
    WHERE EmployeeNumber = @EmployeeNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Departments_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT Department FROM dbo.Employees WHERE IsActive = 1 ORDER BY Department;
END;
GO

---------------------------------------------------------------------------------------------------
-- Attestati
---------------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_Certificates_Get
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Id, e.EmployeeNumber, co.Code AS CourseCode, c.CompletedOn, c.ExpiresOn, c.Score, c.Source
    FROM dbo.Certificates AS c
    JOIN dbo.Employees AS e ON e.Id = c.EmployeeId
    JOIN dbo.Courses AS co ON co.Id = c.CourseId
    WHERE c.Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Certificates_Insert
    @EmployeeNumber NVARCHAR(20),
    @CourseCode     VARCHAR(20),
    @CompletedOn    DATE,
    @Score          DECIMAL(5, 2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EmployeeId INT = (SELECT Id FROM dbo.Employees WHERE EmployeeNumber = @EmployeeNumber AND IsActive = 1);
    IF @EmployeeId IS NULL
        THROW 51002, 'Dipendente inesistente o non attivo.', 1;

    DECLARE @CourseId INT, @ValidityMonths SMALLINT;
    SELECT @CourseId = Id, @ValidityMonths = ValidityMonths FROM dbo.Courses WHERE Code = @CourseCode AND IsActive = 1;
    IF @CourseId IS NULL
        THROW 51003, 'Corso inesistente o disattivato.', 1;

    DECLARE @Inserted TABLE (Id INT);
    INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Score, Source)
    OUTPUT inserted.Id INTO @Inserted
    VALUES (@EmployeeId, @CourseId, @CompletedOn, @ValidityMonths, @Score, 'Manual');

    DECLARE @NewId INT = (SELECT Id FROM @Inserted);
    EXEC dbo.usp_Certificates_Get @Id = @NewId;
END;
GO

-- La modifica e' tracciata automaticamente in CertificatesHistory (tabella temporale).
CREATE OR ALTER PROCEDURE dbo.usp_Certificates_Update
    @Id          INT,
    @CompletedOn DATE,
    @Score       DECIMAL(5, 2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE dbo.Certificates SET CompletedOn = @CompletedOn, Score = @Score WHERE Id = @Id;
    IF @@ROWCOUNT = 0
        RETURN;

    EXEC dbo.usp_Certificates_Get @Id = @Id;
END;
GO

---------------------------------------------------------------------------------------------------
-- Import XML
---------------------------------------------------------------------------------------------------
CREATE TYPE dbo.CertificateImportRowType AS TABLE
(
    LineNumber     INT           NOT NULL PRIMARY KEY,
    EmployeeNumber NVARCHAR(20)  NOT NULL,
    CourseCode     VARCHAR(20)   NOT NULL,
    CompletedOn    DATE          NOT NULL,
    Score          DECIMAL(5, 2) NULL
);
GO

-- Dati di riferimento per la validazione per riga (eseguita nel dominio C#): solo le matricole,
-- i corsi e gli attestati gia' presenti che compaiono nel file.
CREATE OR ALTER PROCEDURE dbo.usp_Imports_Prepare
    @Rows dbo.CertificateImportRowType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EmployeeNumber, e.IsActive
    FROM dbo.Employees AS e
    WHERE e.EmployeeNumber IN (SELECT EmployeeNumber FROM @Rows);

    SELECT co.Code, co.IsActive
    FROM dbo.Courses AS co
    WHERE co.Code IN (SELECT CourseCode FROM @Rows);

    SELECT DISTINCT r.EmployeeNumber, r.CourseCode, r.CompletedOn
    FROM @Rows AS r
    JOIN dbo.Employees AS e ON e.EmployeeNumber = r.EmployeeNumber
    JOIN dbo.Courses AS co ON co.Code = r.CourseCode
    JOIN dbo.Certificates AS c ON c.EmployeeId = e.Id AND c.CourseId = co.Id AND c.CompletedOn = r.CompletedOn;
END;
GO

-- Salva in un'unica transazione il log e le righe accettate. Se nel frattempo un'altra transazione
-- ha inserito lo stesso attestato, il vincolo unique fa fallire tutto (2627) e l'API ripete la validazione.
CREATE OR ALTER PROCEDURE dbo.usp_Imports_Save
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

CREATE OR ALTER PROCEDURE dbo.usp_Imports_GetBySha256
    @Sha256 CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, FileName, Sha256, ImportedAt, ImportedBy, TotalRows, OkRows, RejectedRows
    FROM dbo.ImportLogs WHERE Sha256 = @Sha256;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Imports_List
    @Top INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) Id, FileName, Sha256, ImportedAt, ImportedBy, TotalRows, OkRows, RejectedRows, ErrorsJson
    FROM dbo.ImportLogs
    ORDER BY ImportedAt DESC, Id DESC;
END;
GO
