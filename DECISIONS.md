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
