-- V001: schema di base di Attestia.
-- Convenzioni: identificatori in inglese, chiavi surrogate INT IDENTITY, date "di calendario" come DATE.

CREATE TABLE dbo.Employees
(
    Id             INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeNumber NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Employees_EmployeeNumber UNIQUE,
    FirstName      NVARCHAR(100) NOT NULL,
    LastName       NVARCHAR(100) NOT NULL,
    Email          NVARCHAR(254) NOT NULL,
    Department     NVARCHAR(100) NOT NULL,
    Role           VARCHAR(10)   NOT NULL CONSTRAINT DF_Employees_Role DEFAULT ('Employee'),
    IsActive       BIT           NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    CONSTRAINT CK_Employees_Role CHECK (Role IN ('Employee', 'HR', 'Admin')),
    CONSTRAINT CK_Employees_EmployeeNumber CHECK (EmployeeNumber NOT LIKE '%[^A-Z0-9-]%' AND LEN(EmployeeNumber) >= 2)
);
GO

CREATE INDEX IX_Employees_Department ON dbo.Employees (Department, LastName, FirstName) INCLUDE (EmployeeNumber, IsActive);
GO

CREATE TABLE dbo.Courses
(
    Id             INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Courses PRIMARY KEY,
    Code           VARCHAR(20)   NOT NULL CONSTRAINT UQ_Courses_Code UNIQUE,
    Title          NVARCHAR(200) NOT NULL,
    Category       NVARCHAR(100) NOT NULL,
    ValidityMonths SMALLINT      NULL, -- NULL = l'attestato non scade
    IsMandatory    BIT           NOT NULL CONSTRAINT DF_Courses_IsMandatory DEFAULT (0),
    IsActive       BIT           NOT NULL CONSTRAINT DF_Courses_IsActive DEFAULT (1),
    CONSTRAINT CK_Courses_ValidityMonths CHECK (ValidityMonths IS NULL OR ValidityMonths BETWEEN 1 AND 240),
    CONSTRAINT CK_Courses_Code CHECK (Code NOT LIKE '%[^A-Z0-9-]%' AND LEN(Code) >= 2)
);
GO

CREATE TABLE dbo.ImportLogs
(
    Id           INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ImportLogs PRIMARY KEY,
    FileName     NVARCHAR(260) NOT NULL,
    Sha256       CHAR(64)      NOT NULL CONSTRAINT UQ_ImportLogs_Sha256 UNIQUE,
    ImportedAt   DATETIME2(0)  NOT NULL CONSTRAINT DF_ImportLogs_ImportedAt DEFAULT (SYSUTCDATETIME()),
    ImportedBy   NVARCHAR(20)  NOT NULL,
    TotalRows    INT           NOT NULL,
    OkRows       INT           NOT NULL,
    RejectedRows INT           NOT NULL,
    ErrorsJson   NVARCHAR(MAX) NULL,
    CONSTRAINT CK_ImportLogs_Rows CHECK (TotalRows >= 0 AND OkRows >= 0 AND RejectedRows >= 0 AND OkRows + RejectedRows = TotalRows),
    CONSTRAINT CK_ImportLogs_ErrorsJson CHECK (ErrorsJson IS NULL OR ISJSON(ErrorsJson) = 1),
    CONSTRAINT CK_ImportLogs_Sha256 CHECK (Sha256 NOT LIKE '%[^0-9a-f]%')
);
GO

-- Certificates e' system-versioned: SQL Server conserva in CertificatesHistory ogni versione precedente
-- di una riga (chi ha corretto una data o un punteggio, e quando). ValidityMonths e' una copia della
-- validita' del corso al momento del completamento, cosi' ExpiresOn puo' essere una colonna calcolata
-- PERSISTED (DATEADD su DATE e' deterministica) e non cambia se in seguito si modifica il corso.
CREATE TABLE dbo.Certificates
(
    Id             INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Certificates PRIMARY KEY,
    EmployeeId     INT          NOT NULL CONSTRAINT FK_Certificates_Employees REFERENCES dbo.Employees (Id),
    CourseId       INT          NOT NULL CONSTRAINT FK_Certificates_Courses REFERENCES dbo.Courses (Id),
    CompletedOn    DATE         NOT NULL,
    ValidityMonths SMALLINT     NULL,
    ExpiresOn      AS (CASE WHEN ValidityMonths IS NULL THEN NULL ELSE DATEADD(MONTH, ValidityMonths, CompletedOn) END) PERSISTED,
    Score          DECIMAL(5, 2) NULL,
    Source         VARCHAR(10)  NOT NULL CONSTRAINT DF_Certificates_Source DEFAULT ('Manual'),
    ImportLogId    INT          NULL CONSTRAINT FK_Certificates_ImportLogs REFERENCES dbo.ImportLogs (Id),
    ValidFrom      DATETIME2(7) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidTo        DATETIME2(7) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo),
    CONSTRAINT UQ_Certificates_Employee_Course_CompletedOn UNIQUE (EmployeeId, CourseId, CompletedOn),
    CONSTRAINT CK_Certificates_Score CHECK (Score IS NULL OR Score BETWEEN 0 AND 100),
    CONSTRAINT CK_Certificates_CompletedOn CHECK (CompletedOn >= '2000-01-01'),
    CONSTRAINT CK_Certificates_ValidityMonths CHECK (ValidityMonths IS NULL OR ValidityMonths BETWEEN 1 AND 240),
    CONSTRAINT CK_Certificates_Source CHECK (Source IN ('Manual', 'XmlImport')),
    CONSTRAINT CK_Certificates_SourceImport CHECK ((Source = 'XmlImport' AND ImportLogId IS NOT NULL) OR (Source = 'Manual' AND ImportLogId IS NULL))
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.CertificatesHistory, DATA_CONSISTENCY_CHECK = ON));
GO

-- La data futura non e' un CHECK (sarebbe valutato solo all'inserimento e dipenderebbe dal fuso del server):
-- e' una regola di dominio applicata in C# e ripetuta nella stored procedure di import.

CREATE INDEX IX_Certificates_ExpiresOn ON dbo.Certificates (ExpiresOn) INCLUDE (EmployeeId, CourseId, CompletedOn);
CREATE INDEX IX_Certificates_CourseId ON dbo.Certificates (CourseId, EmployeeId, CompletedOn DESC);
CREATE INDEX IX_Certificates_ImportLogId ON dbo.Certificates (ImportLogId) WHERE ImportLogId IS NOT NULL;
GO

-- Impostazioni applicative lette dal T-SQL (unica fonte per la soglia "in scadenza").
CREATE TABLE dbo.AppSettings
(
    [Key]   VARCHAR(50)   NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
    [Value] NVARCHAR(200) NOT NULL
);
GO

INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES ('ExpiringSoonDays', N'60');
GO
