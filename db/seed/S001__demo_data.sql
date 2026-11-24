-- S001: dati dimostrativi COMPLETAMENTE FITTIZI (nomi inventati, email @example.test).
-- Deterministico: nessun RAND()/NEWID(); ogni valore deriva da formule sugli indici.
-- Le date sono relative al giorno in cui si esegue il seed, cosi' il mix di stati (valido / in scadenza /
-- scaduto / mancante) resta realistico qualunque sia la data di esecuzione.
-- Le matricole E0001..E0003 corrispondono agli utenti Keycloak di sviluppo (dipendente, hr, admin).

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.Employees)
BEGIN
    PRINT 'Seed gia'' presente: nessuna modifica.';
    RETURN;
END;

DECLARE @Today DATE = dbo.fn_BusinessToday();

INSERT INTO dbo.Courses (Code, Title, Category, ValidityMonths, IsMandatory, IsActive)
VALUES ('SIC-GEN',   N'Formazione generale sui rischi',         N'Sicurezza sul lavoro', NULL, 1, 1),
       ('SIC-SPEC',  N'Formazione specifica rischio medio',     N'Sicurezza sul lavoro', 60,   1, 1),
       ('GDPR',      N'Privacy e protezione dei dati',          N'Privacy',              24,   1, 1),
       ('ANTINC',    N'Addetto antincendio',                    N'Emergenze',            60,   0, 1),
       ('PRIMO-SOC', N'Addetto primo soccorso',                 N'Emergenze',            36,   0, 1),
       ('PREPOSTO',  N'Aggiornamento preposti',                 N'Sicurezza sul lavoro', 24,   0, 1),
       ('CARRELLI',  N'Conduzione carrelli elevatori',          N'Attrezzature',         60,   0, 1),
       ('VDT',       N'Uso sicuro dei videoterminali',          N'Sicurezza sul lavoro', 60,   0, 1);

DECLARE @First TABLE (Idx INT PRIMARY KEY, Name NVARCHAR(50));
INSERT INTO @First VALUES (0, N'Aurelio'), (1, N'Bianca'), (2, N'Corrado'), (3, N'Dalia'), (4, N'Emilio'),
    (5, N'Fiamma'), (6, N'Gualtiero'), (7, N'Ilaria'), (8, N'Lapo'), (9, N'Mirella'), (10, N'Nevio'),
    (11, N'Ornella'), (12, N'Pietro'), (13, N'Rebecca'), (14, N'Saverio'), (15, N'Tecla'), (16, N'Ugo'),
    (17, N'Vanna'), (18, N'Zeno'), (19, N'Agnese'), (20, N'Brunone'), (21, N'Cesira');

DECLARE @Last TABLE (Idx INT PRIMARY KEY, Name NVARCHAR(50));
INSERT INTO @Last VALUES (0, N'Amarantini'), (1, N'Brezzolari'), (2, N'Ciottolini'), (3, N'Dorandelli'),
    (4, N'Erbacci'), (5, N'Fagiolari'), (6, N'Gessolini'), (7, N'Lampugnetti'), (8, N'Marzolini'),
    (9, N'Nocciolari'), (10, N'Orticelli'), (11, N'Pagliuzzi'), (12, N'Quercioli'), (13, N'Rondinetti'),
    (14, N'Salvionetti'), (15, N'Tamericini'), (16, N'Vallombri'), (17, N'Zafferanti'), (18, N'Acquerini');

DECLARE @Dept TABLE (Idx INT PRIMARY KEY, Name NVARCHAR(100));
INSERT INTO @Dept VALUES (0, N'Produzione'), (1, N'Logistica'), (2, N'Amministrazione'), (3, N'Commerciale'), (4, N'IT');

-- Utenti di sviluppo collegati a Keycloak
INSERT INTO dbo.Employees (EmployeeNumber, FirstName, LastName, Email, Department, Role, IsActive)
VALUES ('E0001', N'Nadia',  N'Verdolino',   'dipendente@example.test', N'Produzione',      'Employee', 1),
       ('E0002', N'Ettore', N'Calcagnetti', 'hr@example.test',         N'Amministrazione', 'HR',       1),
       ('E0003', N'Livia',  N'Sbaraglini',  'admin@example.test',      N'IT',              'Admin',    1);

-- Altri 57 dipendenti generati (E0004..E0060); gli ultimi due non sono piu' attivi.
WITH N AS (SELECT TOP (57) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + 3 AS n FROM sys.all_objects)
INSERT INTO dbo.Employees (EmployeeNumber, FirstName, LastName, Email, Department, Role, IsActive)
SELECT 'E' + RIGHT('0000' + CAST(N.n AS VARCHAR(4)), 4),
       f.Name, l.Name,
       LOWER(f.Name + '.' + l.Name) + CAST(N.n AS VARCHAR(4)) + '@example.test',
       d.Name,
       CASE WHEN N.n IN (17, 41) THEN 'HR' ELSE 'Employee' END,
       CASE WHEN N.n >= 59 THEN 0 ELSE 1 END
FROM N
JOIN @First AS f ON f.Idx = (N.n * 7) % 22
JOIN @Last AS l ON l.Idx = (N.n * 11) % 19
JOIN @Dept AS d ON d.Idx = (N.n * 3) % 5;

-- Attestati. h decide se l'attestato esiste, h2 quanto e' vecchio (in giorni, rispetto alla validita').
WITH Pairs AS
(
    SELECT e.Id AS EmployeeId, co.Id AS CourseId, co.Code, co.ValidityMonths, co.IsMandatory, e.Department,
           CAST(SUBSTRING(e.EmployeeNumber, 2, 4) AS INT) AS n, co.Id AS k
    FROM dbo.Employees AS e CROSS JOIN dbo.Courses AS co
),
Hashed AS
(
    SELECT p.*, (p.n * 37 + p.k * 101) % 100 AS h, (p.n * 53 + p.k * 17) % 100 AS h2
    FROM Pairs AS p
),
Selected AS
(
    SELECT *
    FROM Hashed
    WHERE (IsMandatory = 1 AND h >= 6)
       OR (Code = 'ANTINC' AND h < 35)
       OR (Code = 'PRIMO-SOC' AND h < 30)
       OR (Code = 'PREPOSTO' AND Department IN (N'Produzione', N'Logistica') AND h < 45)
       OR (Code = 'CARRELLI' AND Department = N'Logistica' AND h < 75)
       OR (Code = 'VDT' AND Department IN (N'Amministrazione', N'Commerciale', N'IT') AND h < 60)
),
Dated AS
(
    SELECT s.*,
           CASE WHEN s.ValidityMonths IS NULL THEN 30 + s.h2 * 30
                ELSE 15 + (s.h2 * (s.ValidityMonths * 31 + 120)) / 100 END AS AgeDays
    FROM Selected AS s
)
INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Score, Source)
SELECT EmployeeId, CourseId, DATEADD(DAY, -AgeDays, @Today), ValidityMonths,
       CASE WHEN h % 4 = 0 THEN NULL ELSE CAST(60 + (h * 7) % 41 AS DECIMAL(5, 2)) END,
       'Manual'
FROM Dated
UNION ALL
-- Rinnovi: per circa un attestato su quattro esiste anche il completamento precedente (storico).
SELECT EmployeeId, CourseId, DATEADD(DAY, -(AgeDays + ValidityMonths * 30 + 10), @Today), ValidityMonths,
       CAST(55 + (h2 * 3) % 46 AS DECIMAL(5, 2)),
       'Manual'
FROM Dated
WHERE ValidityMonths IS NOT NULL AND h2 < 25;

-- La dipendente di sviluppo (E0001) ha un attestato in scadenza e uno scaduto, utili per la demo del portale.
UPDATE c SET CompletedOn = DATEADD(DAY, 20, DATEADD(MONTH, -c.ValidityMonths, @Today))
FROM dbo.Certificates AS c
JOIN dbo.Employees AS e ON e.Id = c.EmployeeId
JOIN dbo.Courses AS co ON co.Id = c.CourseId
WHERE e.EmployeeNumber = 'E0001' AND co.Code = 'GDPR'
  AND c.CompletedOn = (SELECT MAX(c2.CompletedOn) FROM dbo.Certificates AS c2 WHERE c2.EmployeeId = c.EmployeeId AND c2.CourseId = c.CourseId);

IF NOT EXISTS (SELECT 1 FROM dbo.Certificates AS c JOIN dbo.Employees AS e ON e.Id = c.EmployeeId
               JOIN dbo.Courses AS co ON co.Id = c.CourseId WHERE e.EmployeeNumber = 'E0001' AND co.Code = 'PRIMO-SOC')
    INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Score, Source)
    SELECT e.Id, co.Id, DATEADD(DAY, -45, DATEADD(MONTH, -co.ValidityMonths, @Today)), co.ValidityMonths, 88, 'Manual'
    FROM dbo.Employees AS e CROSS JOIN dbo.Courses AS co
    WHERE e.EmployeeNumber = 'E0001' AND co.Code = 'PRIMO-SOC';
