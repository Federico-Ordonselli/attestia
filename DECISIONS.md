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

## D22 — Libreria SAML: ITfoxtec.Identity.Saml2
- **Cosa**: ITfoxtec.Identity.Saml2 4.22 (target `net10.0` nativo, rilasci frequenti).
- **Perché**: espone i messaggi SAML come oggetti (`Saml2AuthnRequest`, `Saml2AuthnResponse`, binding Redirect/POST) senza imporre un middleware di autenticazione: serve proprio questo, perché la sessione non è il cookie di ASP.NET ma il cookie `attestia_sso` condiviso con ASP classic. Permette anche di fare da IdP: i test generano risposte firmate con un certificato in memoria.
- **Scartate**: Sustainsys.Saml2 v2 (ottima, ma pensata come handler di autenticazione ASP.NET con propria sessione; la v3 non era stabile).

## D23 — Ponte verso ASP classic: cookie `attestia_sso` firmato HMAC
- **Formato**: `v1|matricola|ruolo|exp|hmac`, HMAC-SHA256 esadecimale minuscolo sui byte UTF-8 del payload, chiave base64 ≥ 32 byte da `ATTESTIA_SSO_KEY`. Verifica a tempo costante, firma controllata **prima** di interpretare il contenuto, scadenza 60 minuti, `HttpOnly; Secure; SameSite=Lax; Path=/`.
- **Una sola sessione**: lo stesso cookie è anche lo schema di autenticazione dell'API (`SsoCookieAuthenticationHandler`), quindi back-office e portale scadono insieme e il logout vale per entrambi.
- **Header scritto a mano**: `Response.Cookies.Append` farebbe URL-encoding (`|` → `%7C`) e il portale classic leggerebbe byte diversi da quelli firmati. L'alfabeto del valore è ristretto a `[A-Za-z0-9|-]`.
- **Vettore di test** calcolato con Python (indipendente da .NET), usato dai test e dalla checklist Windows per verificare l'implementazione VBScript.
- **Perché non validare SAML in ASP classic**: VBScript non ha una libreria XML-DSig affidabile; validare firme XML (canonicalizzazione, wrapping attack) a mano sarebbe la parte più rischiosa di tutto il progetto. **Alternative valutate**: (a) Shibboleth SP come modulo IIS (robusto, ma un secondo SP da installare e configurare, e la sessione resterebbe separata dall'API); (b) reverse proxy che inietta header di identità (richiede ARR e un canale fidato tra proxy e applicazione); (c) token JWT invece di HMAC (in VBScript servirebbe comunque l'HMAC, con più parsing); (d) sessione lato server condivisa in SQL (stato in più, query a ogni pagina). L'HMAC con chiave condivisa è il minimo indispensabile, verificabile con le classi COM di .NET Framework.
- ⚠️ **Limite noto**: chi ha la chiave può emettere sessioni; la chiave va trattata come un segreto di produzione (variabile d'ambiente dell'application pool, mai nel repository) e ruotata invalidando tutte le sessioni.

## D24 — Dettagli del Service Provider
- **InResponseTo**: l'ID dell'AuthnRequest è conservato in un cookie cifrato e a tempo (Data Protection, 10 minuti, `SameSite=None` perché l'ACS riceve un POST cross-site dall'IdP); risposte non sollecitate o per un'altra richiesta sono rifiutate. Firma, audience, issuer, validità temporale e replay sono verificati da ITfoxtec.
- **Fiducia nel certificato**: pinning del certificato pubblicato nei metadata dell'IdP (Keycloak usa un certificato autofirmato), senza validazione di catena. I metadata sono letti alla prima richiesta e tenuti in cache un'ora.
- **Autorizzazione al login**: oltre ai claim SAML, la matricola deve esistere ed essere attiva nel database. Il **ruolo** viene dall'IdP (fonte di verità per l'identità); la colonna `Employees.Role` è informativa.
- **AuthnRequest non firmate** (`saml.client.signature=false` in Keycloak): firmarle richiederebbe un certificato SP da distribuire anche all'IdP; il rischio residuo (richieste di login forgiate) è basso. ⚠️ In produzione si può attivare con un certificato SP.
- **IdP in HTTP in sviluppo** (`http://localhost:8081`): accettabile solo in locale; la sicurezza delle asserzioni sta nella firma, ma in produzione i metadata vanno letti in HTTPS.
- **Logout**: chiude il cookie locale e invia una LogoutRequest all'IdP (NameID e SessionIndex salvati in un cookie cifrato sotto `/sso`). Gestisce sia il binding Redirect sia il POST (Keycloak con "force POST binding" usa il POST anche per il logout: scoperto provando con Keycloak reale). La LogoutResponse non viene validata: porta solo a una pagina statica, la sessione locale è già chiusa.
- **Strumento CLI** `sso-cookie <matricola> <ruolo> [--expires-in s]`: genera cookie validi o scaduti per la checklist manuale. Non è esposto via HTTP.

## D25 — CSRF
- Tutte le richieste di scrittura su `/api` (JSON e multipart) richiedono `X-XSRF-TOKEN`, il token antiforgery di ASP.NET Core legato all'utente. `/api/me` emette il cookie leggibile `XSRF-TOKEN`, che Angular rimanda automaticamente (supporto XSRF integrato di `HttpClient`). Il middleware gira dopo l'autorizzazione: una richiesta anonima riceve 401, non un errore CSRF. Si somma a `SameSite=Lax` del cookie di sessione.
- `/sso/acs` è escluso per natura (POST cross-site dall'IdP, protetto dalla firma e da InResponseTo).

## D26 — Back-office Angular
- **Versione e stile**: Angular 22, componenti standalone, `OnPush`, change detection *zoneless*, signals per lo stato (`LoadState<T>`: dati/caricamento/errore uniformi in tutte le pagine), TypeScript strict, Vitest (runner predefinito di Angular 22).
- **Client tipizzato generato** con `ng-openapi-gen` da `contracts/openapi/attestia-api.json` (prodotto a build-time dall'API). Per un client pulito l'API dichiara tag in inglese e `operationId` espliciti (`WithName`); uno *schema transformer* marca `required` le proprietà non annullabili, altrimenti ogni campo sarebbe opzionale in TypeScript. `npm run check:api` fallisce se il client non è allineato al contratto.
- **Stato nell'URL**: filtri e pagina delle scadenze sono parametri di query, quindi la vista si può condividere e il tasto "indietro" funziona.
- **Interceptor**: l'ordine conta. Gli errori risalgono la catena al contrario, quindi il redirect su 401 sta *dentro* la conversione in `ApiError` (bug trovato da un test unitario).
- **Import in due passi**: anteprima (`dryRun`) con esito riga per riga, poi conferma esplicita.
- **Nessun dialog del browser** (`window.confirm`): la disattivazione di un corso usa una conferma inline, accessibile e testabile.
- **CSP senza `unsafe-inline` per gli script**: l'inlining del CSS critico di Angular genera un `onload` inline ed è disattivato; gli stili inline restano ammessi perché Angular inserisce `<style>` dei componenti a runtime.
- **Bootstrap 5 da SCSS, solo i moduli usati** (bundle iniziale 461 kB, sotto il budget di 500 kB), con colori corretti per il contrasto AA. Axe ha trovato un problema reale (link della navbar scura al 55% di opacità) ed è stato corretto.
- **In sviluppo l'API serve `/admin`** dalla build Angular, replicando il layout IIS (stessa origine per cookie e XSRF). Il fallback SPA usa `{**path:nonfile}`: senza il vincolo catturerebbe anche `main.js` (gli endpoint sono risolti prima dei file statici).

## D27 — E2E contro lo stack reale
- Playwright esegue il login SAML vero su Keycloak, naviga, importa un XML (anteprima, conferma, rifiuto del duplicato) e controlla con axe-core le regole WCAG 2.0/2.1 A e AA su cinque schermate. Il file di import ha un `exportId` univoco per esecuzione, quindi il test è ripetibile.
- Limite: axe trova solo i problemi rilevabili automaticamente (circa un terzo dei criteri WCAG); ordine del focus, comprensibilità dei testi e uso con screen reader richiedono una verifica manuale.
- Rate limit dell'upload: 429 con ProblemDetails in italiano e `Retry-After`. In sviluppo è a 30 al minuto, perché più giri di e2e ravvicinati superavano il limite di produzione (5).

## D28 — Rotazione delle chiavi dell'IdP
- Scoperta ricreando il container Keycloak: i metadata in cache contenevano il vecchio certificato e ogni login falliva per "firma non valida" fino alla scadenza della cache (1 ora).
- Ora, se la firma non è valida, il provider rilegge i metadata e la validazione viene ripetuta **una** volta. Il ricaricamento è concesso al massimo una volta al minuto, così risposte forgiate non possono forzare download continui. Effetto residuo: dopo una rotazione i login possono fallire per al massimo un minuto.

## D29 — Portale ASP classic
- **Include senza direttive**: la direttiva `<%@ %>` e `Option Explicit` stanno solo nelle pagine. Gli include vengono copiati testualmente nella pagina, quindi ripeterli causerebbe ASP 0141 e un secondo `Option Explicit`. La prima versione (scritta con Codex) li ripeteva e li rimuoveva con uno script Python durante il packaging: funzionava, ma il sorgente non era distribuibile così com'era. Ora è un controllo di `check_portal.py`, nei due sensi.
- **Rendering lato server + arricchimento jQuery**: la tabella è completa anche senza JavaScript; DataTables aggiunge ricerca e ordinamento (date ordinate tramite `data-order` ISO), e il pulsante «Aggiorna» ricarica i dati da `certificates-json.asp` costruendo le celle con `.text()`, mai con HTML concatenato.
- **JSON costruito a mano** (`JsonString`): escape di `"`, `\`, `/`, caratteri di controllo e anche `< > & '` come `\u00XX`, quindi sicuro anche se finisse in un contesto HTML. I numeri usano il punto decimale indipendentemente dalla locale del server.
- **HMAC via COM .NET Framework** (`System.Security.Cryptography.HMACSHA256`, `UTF8Encoding`) e base64 via MSXML 6. Il pool del portale carica il CLR v4. La funzione `HmacSha256Hex` è separata dalla lettura della chiave, così `legacy/portale-tests/hmac-selftest.asp` (fuori dal pacchetto, risponde solo in locale) verifica il calcolo VBScript sul vettore indipendente calcolato con Python.
- **"Oggi" in UTC**: VBScript non ha un orologio UTC; `UnixNow()` è una funzione JScript nello stesso include (`<script language="JScript" runat="server">`).
- **Bootstrap 3 voluto**: rappresenta un portale preesistente, è fuori supporto (EOL 2019) e in produzione andrebbe migrato. I colori dei `label` sono ridefiniti per il contrasto AA.
- **Controlli statici su Linux** (`scripts/check_portal.py`): UTF-8 senza BOM, CRLF, direttive e `Option Explicit`, niente SQL nelle stringhe VBScript, nomi di procedure, parametri e colonne confrontati con le migrazioni, ogni output protetto da `H()`/`JsonString()`, costanti del cookie confrontate con `SsoCookie.cs`, nessuna risorsa esterna. Verificati con test di mutazione (un errore introdotto apposta viene segnalato).
