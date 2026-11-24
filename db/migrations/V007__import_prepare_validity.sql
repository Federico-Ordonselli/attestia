-- V007: usp_Imports_Prepare restituisce anche ValidityMonths, cosi' il report di import (e l'anteprima)
-- mostra per ogni riga accettata la scadenza e lo stato calcolati con le regole di dominio.

CREATE OR ALTER PROCEDURE dbo.usp_Imports_Prepare
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
