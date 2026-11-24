# Decisioni tecniche

Ogni voce: **cosa** si è deciso, **perché**, **alternative scartate**. Le voci marcate ⚠️ meritano una conferma.

## D01 — Versioni di piattaforma
- **Cosa**: .NET 10 (LTS, SDK 10.0.114), Angular 22.3, SQL Server 2022 (immagine `2022-latest`), Keycloak 26.8.2, Node 24.
- **Perché**: le ultime LTS/stabili installate o disponibili al momento dell'esecuzione (novembre 2026).
- **Scartate**: .NET 11 (STS, non LTS); SQL Server 2025 (la specifica chiede 2022).

## D02 — Accesso ai dati: Dapper + stored procedure per *tutto*
- **Cosa**: ogni operazione passa da una stored procedure; il C# esegue solo `EXEC` tramite Dapper (`CommandType.StoredProcedure`).
- **Perché**: il T-SQL è parte del progetto, i parametri sono sempre tipizzati, e il login applicativo può avere solo `EXECUTE` (nessun accesso diretto alle tabelle, vedi D09).
- **Scartate**: EF Core (escluso dalla specifica); SQL inline parametrizzato per il CRUD semplice (più veloce da scrivere, ma richiede permessi sulle tabelle).

## D03 — Migrazioni con DbUp, script incorporati
- **Cosa**: `db/migrations/V###__*.sql` (journal `SchemaVersions`) e `db/seed/S###__*.sql` (journal `SeedVersions`) sono risorse incorporate in `Attestia.Data`. Comando: `dotnet run --project src/Attestia.Api -- migrate --seed`; all'avvio se `Database:MigrateOnStartup=true`.
- **Perché**: DbUp esegue T-SQL puro (incluso `GO`), tiene un journal e non impone un modello; incorporare gli script rende il runner indipendente dalla cartella di lavoro (anche su IIS).
- **Scartate**: Flyway (richiede JVM o un container in più); `sqlpackage`/DACPAC (meno leggibile per una revisione).

## D04 — `ExpiresOn` come colonna calcolata persistita su una copia di `ValidityMonths`
- **Cosa**: `Certificates.ValidityMonths` è copiato dal corso all'inserimento; `ExpiresOn AS DATEADD(MONTH, ValidityMonths, CompletedOn) PERSISTED`.
- **Perché**: una colonna calcolata non può leggere un'altra tabella; inoltre la validità di un attestato è quella in vigore quando è stato conseguito: cambiare la durata di un corso non deve modificare in silenzio gli attestati storici.
- **Scartate**: trigger che ricalcola `ExpiresOn` (logica nascosta); vista con join sul corso (scadenze retroattive).

## D05 — Regole di stato e loro unica definizione
- **Cosa**: `ExpiresOn` è l'**ultimo giorno di validità**. `NULL` → Valid; `< oggi` → Expired; `≤ oggi + soglia` → ExpiringSoon; altrimenti Valid; corso obbligatorio senza attestati → Missing. Soglia di default 60 giorni in `dbo.AppSettings` (`ExpiringSoonDays`), sovrascrivibile per chiamata.
- **Perché**: definizione unica in `dbo.fn_CertificateStatus` (inline TVF, quindi espansa nel piano senza costo di UDF scalare) + `fn_ComplianceMatrix` (attestato più recente per dipendente/corso). La copia C# serve all'API per stati calcolati senza round-trip (es. anteprima import) ed è vincolata da un test di parità.
- ⚠️ **Da confermare**: "scade il 10/03" significa valido fino al 10/03 incluso. L'alternativa (scaduto dal 10/03) è un cambio di una riga in entrambe le implementazioni.

## D06 — "Oggi" nel fuso aziendale
- **Cosa**: `dbo.fn_BusinessToday()` usa `AT TIME ZONE 'W. Europe Standard Time'`; in C# `IBusinessClock` usa `Europe/Rome`. Le procedure accettano `@AsOf` esplicito (l'API lo passa sempre, i test lo fissano).
- **Perché**: il server SQL in Docker è in UTC; tra mezzanotte e l'una/le due un attestato risulterebbe scaduto un giorno prima.

## D07 — Seed deterministico con date relative
- **Cosa**: nessun `RAND()`/`NEWID()`; stati derivati da formule sugli indici; date calcolate rispetto al giorno del seed.
- **Perché**: stesse righe a ogni esecuzione, ma un mix di stati realistico anche fra un anno. I test di integrazione non dipendono dal seed (creano i propri dati con `@AsOf` fisso).

## D08 — Il controllo "data futura" non è un CHECK constraint
- **Perché**: un CHECK con `GETDATE()` viene valutato solo in scrittura e dipende dal fuso del server; è una regola di dominio (C#) applicata all'inserimento manuale e all'import.

## D09 — Ruoli di database a privilegi minimi
- **Cosa**: `attestia_api` (EXECUTE sullo schema `dbo`), `attestia_portal` (EXECUTE solo su `usp_EmployeeCertificateHistory`). Le migrazioni girano con un account proprietario.
- **Perché**: il portale classic, il componente più esposto e meno moderno, non può leggere altro che lo storico di un dipendente. L'associazione login → ruolo dipende dal server ed è nello script IIS.

## D10 — Strumenti di qualità .NET
- **Cosa**: `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, gestione centralizzata dei pacchetti (`Directory.Packages.props`), `NuGetAudit` in modalità `all`.
- CA1873 abbassato a *suggestion*: segnala anche chiamate a metodi `[LoggerMessage]` già protette da `IsEnabled` (falso positivo).

## D11 — Porte di sviluppo
- API `https://localhost:7180`, Keycloak `http://localhost:8081` (la 8080 è occupata sulla macchina di sviluppo), SQL Server `127.0.0.1:1433`. Tutte legate a 127.0.0.1.

## D12 — Periodo della tabella temporale in `DATETIME2(7)`
- **Perché**: con `DATETIME2(2)` un inserimento e una correzione nello stesso centesimo di secondo generano una versione a durata zero, che SQL Server esclude dalle query `FOR SYSTEM_TIME`. Scoperto da un test di integrazione.

## D13 — Strategia di test .NET
- **Cosa**: xUnit v3 su Microsoft.Testing.Platform (`global.json`), Testcontainers con **un solo** SQL Server 2022 per l'assembly (`AssemblyFixture`) e **un database nuovo per classe di test** (`ClassDatabase`), migrato con lo stesso runner usato in produzione.
- **Perché**: test realistici (T-SQL, vincoli, tabella temporale, permessi) senza pagare l'avvio di un container per classe, e senza interferenze fra classi. I test di integrazione sono marcati `Category=Integration` per poterli escludere senza Docker.
- **Scartate**: docker-compose condiviso con lo sviluppo (stato sporco, porte fisse); SQLite/LocalDB (non è SQL Server su Linux).

## D14 — Minimal API (non controller)
- **Cosa**: endpoint organizzati per area con `MapGroup` (`Endpoints/*.cs`), `TypedResults`, validazione integrata di .NET 10 (`AddValidation()` + DataAnnotations, anche sui parametri di query).
- **Perché**: meno cerimonia, tipi di risposta espliciti (`Results<Ok<T>, NotFound>`) che arricchiscono l'OpenAPI generato, policy di autorizzazione dichiarate accanto alla rotta. La validazione nativa produce `HttpValidationProblemDetails` senza filtri custom.
- **Scartate**: controller MVC (più adatti a grandi superfici con filtri condivisi; qui non servono); FluentValidation (dipendenza in più per regole semplici).

## D15 — Autorizzazione: policy per ruolo + fallback obbligatorio
- **Cosa**: policy `AnyUser` / `Hr` (HR, Admin) / `Admin`; `FallbackPolicy = AnyUser`, quindi ogni endpoint nuovo è protetto per default (anonimi solo `/health`, `/openapi`, `/sso/*`).
- Un Employee legge solo `/api/me/certificates`: la matricola è letta dal claim. `/api/employees/{number}/certificates` è solo HR, anche per la propria matricola: un endpoint con la matricola in URL non deve mai dipendere da un controllo "è la mia?".
- **Aggiunta rispetto alla specifica**: `GET /api/me/certificates` e `GET /api/departments` (filtri delle schermate), `GET /api/certificates/{id}` (destinazione di `Location` dopo il POST).

## D16 — Soglia "in scadenza" e data di riferimento
- La soglia (60 giorni) vive solo in `dbo.AppSettings`; l'API non la duplica in configurazione. L'API passa sempre `@AsOf` calcolato da `IBusinessClock` (fuso Europe/Rome), così i test usano un `FakeTimeProvider`.

## D17 — Errori
- `IExceptionHandler` unico: `ConflictException` → 409, `UnprocessableException` → 422, tutto il resto → 500 generico con `traceId` (il dettaglio va solo nei log). Gli errori SQL attesi (THROW 51xxx, violazioni unique 2627/2601) sono tradotti nel livello dati, non nell'API.
- Data di completamento futura → 422: è una regola di dominio (D08), non un errore di formato.

## D18 — Parsing XML sicuro
- `XmlReader` in streaming con `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, `MaxCharactersInDocument` (2 MB), profondità massima 8, e **nessun** `ProcessInlineSchema`/`ProcessSchemaLocation`: è il server a scegliere lo schema, non il file. Gli XSD stessi sono letti con le stesse restrizioni.
- Le date usano un tipo `CalendarDate` (xs:date senza fuso): `2026-01-31+01:00` è valido per `xs:date` ma ambiguo per una data di calendario.
- Una riga che non si riesce a interpretare produce sempre un errore (mai scartata in silenzio): questa difesa ha scoperto un bug reale di doppio avanzamento con `ReadElementContentAsString`.
- Verifica incrociata: gli stessi esempi sono validati anche con `xmllint` (libxml2), che segnala le stesse righe.

## D19 — Import idempotente e transazionale
- Tre livelli: (1) SHA-256 del file unico in `ImportLogs` → stesso file rifiutato con 409 e riferimento all'import precedente; (2) righe già presenti scartate con motivo "Attestato già registrato" (stesso contenuto in un file diverso non crea duplicati); (3) vincolo unique `(EmployeeId, CourseId, CompletedOn)` come garanzia finale.
- Righe valide e log in **un'unica transazione** (`usp_Imports_Save`, TVP). Se un import concorrente inserisce le stesse righe tra validazione e salvataggio, la transazione fallisce per intero e la validazione viene ripetuta (max 3 tentativi): coperto da un test con due upload paralleli.
- Un file che non rispetta l'XSD viene rifiutato per intero (422 con elenco errori per riga) e **non** viene registrato: altrimenti il suo SHA impedirebbe di ricaricarlo dopo la correzione... che avrebbe comunque un hash diverso, ma lo storico si riempirebbe di tentativi falliti.
- **Aggiunta**: `dryRun=true` restituisce il report riga per riga senza salvare nulla (è l'"anteprima" della schermata di import).

## D20 — Table type con progressivo di riga (V008)
- La chiave del TVP era il numero di riga del file; un XML scritto su una sola riga lo ripete. Ora la chiave è `RowNumber` (posizione nel file) e `LineNumber` resta per i messaggi. Il tipo è ricreato in una nuova migrazione, non modificando la V004.

## D21 — Rate limiting dell'upload
- 5 upload al minuto per utente (finestra fissa, configurabile con `RateLimits:UploadsPerMinute`), 429 oltre la soglia. Limite di dimensione 2 MB verificato sia sul corpo multipart sia sul file (413), più `maxAllowedContentLength` in IIS.
