-- V005: ruoli di database a privilegi minimi.
-- Le migrazioni girano con un account proprietario; a runtime:
--   attestia_api    -> l'API ASP.NET Core: solo EXECUTE sulle procedure (nessun accesso diretto alle tabelle)
--   attestia_portal -> il portale ASP classic: solo EXECUTE su usp_EmployeeCertificateHistory
-- L'associazione login -> ruolo (es. IIS APPPOOL\AttestiaApi) e' fatta in fase di installazione
-- (deploy/iis/Install-Attestia.ps1), non qui, perche' dipende dal server.

IF DATABASE_PRINCIPAL_ID('attestia_api') IS NULL
    CREATE ROLE attestia_api AUTHORIZATION dbo;
GO
IF DATABASE_PRINCIPAL_ID('attestia_portal') IS NULL
    CREATE ROLE attestia_portal AUTHORIZATION dbo;
GO

GRANT EXECUTE ON SCHEMA::dbo TO attestia_api;
GRANT EXECUTE ON TYPE::dbo.CertificateImportRowType TO attestia_api;
GRANT EXECUTE ON OBJECT::dbo.usp_EmployeeCertificateHistory TO attestia_portal;
GO
