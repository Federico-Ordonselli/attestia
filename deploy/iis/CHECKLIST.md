# Collaudo IIS su Windows Server 2022 o Windows 11

**Stato: da verificare su Windows.** Usare un ambiente di sviluppo con dati fittizi. Spuntare l'esito solo dopo aver osservato il risultato atteso. I comandi PowerShell presuppongono una console Windows PowerShell 5.1 a 64 bit avviata come amministratore, nella cartella contenente `Install-Attestia.ps1`.

1. **Preparare il pacchetto.** Su Linux, dalla radice, eseguire `python3 scripts/check_portal.py` e `deploy/iis/build-package.sh` con SDK .NET 10, Python 3 e, per Angular, Node e npm. Copiare `deploy/iis/out` su Windows in `C:\pacchetti\attestia`. **Atteso** — Le tre sottocartelle hanno ciascuna un `web.config`, l'API contiene `Attestia.Api.dll` e il portale contiene `default.asp`. Per il collaudo completo devono essere presenti `admin/index.html`, le rotte SSO e il comando `sso-cookie` della versione API in prova. **Esito** ☐

2. **Verificare i prerequisiti.** Abilitare IIS, installare o riparare il Hosting Bundle .NET 10 dopo IIS, installare URL Rewrite 2.1 e Microsoft OLE DB Driver 19 x64. Verificare VBScript, MSXML 6 e la disponibilità COM delle classi .NET Framework `System.Security.Cryptography.HMACSHA256` e `System.Text.UTF8Encoding`. Predisporre SQL Server con certificato attendibile e un account proprietario per le migrazioni. Usare SQL Server sulla macchina IIS per i login virtuali `IIS APPPOOL`. **Atteso** — `dotnet --list-runtimes` elenca `Microsoft.AspNetCore.App 10`, i moduli risultano installati, l'account proprietario si collega al database. **Esito** ☐

3. **Preparare SAML e risoluzione del nome.** Far risolvere `attestia.local` all'indirizzo IIS. Rendere raggiungibile Keycloak (o un IdP SAML equivalente) sia dal browser sia dall'API. Il realm di sviluppo accetta già `https://attestia.local/sso/*` tra le destinazioni del client `urn:attestia:sp`; nella console di Keycloak impostare però l'URL di logout del client a `https://attestia.local/sso/logout` (quello predefinito punta all'API di sviluppo su Linux). L'URL dei metadati dell'IdP si passa all'installazione con `-IdpMetadataUrl` (il certificato di firma è letto da lì). **Atteso** — `https://attestia.local/sso/metadata` mostra entityID `urn:attestia:sp` e ACS `https://attestia.local/sso/acs`; SP e IdP concordano su issuer, destinatario e attributi `employeeNumber` e `role`. **Esito** ☐

4. **Simulare l'installazione.** Preparare una chiave casuale di almeno 32 byte già codificata in base64, conservarla nel gestore dei segreti e inserirla al prompt protetto. Usare destinazioni dedicate, poiché il mirror elimina i file estranei al pacchetto.

   ```powershell
   $key = Read-Host 'Chiave SSO in base64' -AsSecureString
   $install = @{
       PackagePath = 'C:\pacchetti\attestia'; SsoKey = $key; TrustDevelopmentCertificate = $true
       IdpMetadataUrl = 'http://keycloak.local:8081/realms/attestia/protocol/saml/descriptor'
       TrustSqlServerCertificate = $true   # solo se SQL Server usa un certificato autofirmato
   }
   .\Install-Attestia.ps1 @install -WhatIf
   ```

   **Atteso** — Sono elencate le operazioni previste. File, archivio certificati, configurazione IIS e SQL conservano lo stato iniziale. La chiave resta riservata. **Esito** ☐

5. **Installare e ripetere.** Eseguire `.\Install-Attestia.ps1 @install` due volte. Accettare la fiducia nel certificato autofirmato soltanto sulle macchine di sviluppo, impostando `TrustDevelopmentCertificate`. Sui client di prova remoti importare la sola parte pubblica se necessaria. **Atteso** — Un sito con binding HTTP per il reindirizzamento e HTTPS con SNI, un certificato riutilizzato e tre pool. La radice serve l'API, `/portale` e `/admin` sono applicazioni figlie su cartelle sorelle. **Esito** ☐

6. **Applicare migrazioni e permessi SQL.** In una console aperta con l'account proprietario, impostare la stringa di connessione per il processo e avviare il comando dalla cartella API.

   ```powershell
   $env:ConnectionStrings__Attestia = 'Server=.;Database=Attestia;Integrated Security=True;Encrypt=True;'
   Set-Location C:\inetpub\attestia\api
   dotnet Attestia.Api.dll migrate
   ```

   Per il collaudo con i dati dimostrativi eseguire anche `dotnet Attestia.Api.dll migrate --seed`. Dalla console dell'installazione ripetere `.\Install-Attestia.ps1 @install -ConfigureSqlLogins`. In assenza del modulo `SqlServer` eseguire il file `Configure-SqlLogins.sql` generato accanto all'installatore con un amministratore SQL. **Atteso** — Migrazioni registrate una sola volta e identità dei pool associate ai due ruoli di V005. **Esito** ☐

7. **Accedere tramite Keycloak.** Aprire `http://attestia.local/` in una finestra privata. Accedere a turno come `dipendente`, `hr` e `admin`, uscendo tra le prove. La password di questi utenti fittizi è `Dev-Only-Password-1`. **Atteso** — Passaggio a HTTPS, `/portale/`, accesso SAML e ritorno al portale. In `/health` l'API risponde correttamente. **Esito** ☐

8. **Ispezionare cookie e uscita.** Negli strumenti di sviluppo controllare la risposta dell'ACS e il cookie `attestia_sso`. Premere «Esci dal portale». **Atteso** — `HttpOnly`, `Secure`, `SameSite=Lax` e `Path=/` presenti. Dopo l'uscita il browser riceve la scadenza del cookie e segue `/sso/logout`. **Esito** ☐

9. **Controllare lo storico personale.** Per ciascun ruolo confrontare matricola e corsi con il relativo storico SQL. Richiedere anche `/portale/?employeeNumber=E0002` con l'utente `dipendente`. Disabilitare JavaScript, poi riabilitarlo e provare ricerca, ordinamento date e «Aggiorna». **Atteso** — Ogni ruolo vede solo il proprio storico. Tabella completa senza JavaScript, stati leggibili, «Rinnovato» per le righe superate, riepilogo delle righe correnti, date ordinate cronologicamente e dati coerenti dopo l'aggiornamento. Con uno storico vuoto compare «Nessun attestato registrato». **Esito** ☐

10. **Controllare l'endpoint JSON.** Da autenticati aprire `/portale/certificates-json.asp` e analizzare la risposta con gli strumenti di sviluppo. Ripetere con `curl.exe -i https://attestia.local/portale/certificates-json.asp` senza cookie. **Atteso** — Per la prima richiesta JSON valido con matricola, istante UTC e righe tipizzate. Per la seconda HTTP 401 con corpo JSON. Entrambe hanno `Cache-Control: no-store` e codifica UTF-8. **Esito** ☐

11. **Verificare l'HMAC VBScript con il vettore di test.** Copiare temporaneamente `legacy/portale-tests/hmac-selftest.asp` in `C:\inetpub\attestia\portale\` e aprire `https://localhost/portale/hmac-selftest.asp` **dal server stesso** (la pagina risponde solo a richieste locali). Rimuovere il file subito dopo. **Atteso** — `HMAC OK` (lo stesso valore `bf8110b6…791d` calcolato da Python e verificato dai test .NET) e `UnixNow OK`. Se compare `HMAC DIVERSO DAL VETTORE`, il portale rifiuterebbe ogni cookie: non proseguire. **Esito** ☐

12. **Verificare una firma alterata.** Negli strumenti di sviluppo modificare un solo carattere esadecimale della firma del proprio cookie e richiedere `/portale/`. Osservare la prima risposta nella scheda rete. **Atteso** — Reindirizzamento a `/sso/login` con `returnUrl` che decodifica in `/portale/`. **Esito** ☐

13. **Verificare la scadenza.** Nella cartella API, con la stessa `ATTESTIA_SSO_KEY` disponibile al processo CLI, eseguire `dotnet Attestia.Api.dll sso-cookie E0001 Employee --expires-in -60`. Inserire il valore negli strumenti di sviluppo come cookie del sito con `Path=/` e aggiornare `/portale/`. **Atteso** — Anche con firma valida la risposta iniziale reindirizza al login. **Esito** ☐

14. **Aprire la pagina di diniego.** Richiedere `/portale/denied.asp` e raggiungere il collegamento con la tastiera. **Atteso** — HTTP 403, messaggio italiano e collegamento di uscita con focus visibile. **Esito** ☐

15. **Verificare il back-office e l'importazione.** Con il ruolo HR aprire `/admin/`, navigare a `/admin/scadenze` e aggiornare direttamente la pagina. Importare dal back-office `contracts/samples/import-valid.xml` e verificare l'esito registrato. **Atteso** — Il percorso profondo carica Angular, gli asset provengono da `/admin/` e l'API accetta l'importazione autorizzata. **Esito** ☐

16. **Verificare il ruolo Employee.** Accedere come `dipendente` e tentare la stessa pagina HR e la relativa operazione API. **Atteso** — L'interfaccia presenta il diniego. La richiesta protetta all'API restituisce HTTP 403 e i dati HR rimangono inaccessibili. Il documento statico iniziale di Angular può rispondere con 200. **Esito** ☐

17. **Controllare intestazioni e cache.** Eseguire `curl.exe -I` su `https://attestia.local/health`, `/portale/default.asp` e `/admin/index.html`. Ripetere su un asset Angular con hash e su un file del portale sotto `/portale/assets/`. **Atteso** — HSTS, `nosniff`, `no-referrer`, `DENY` e CSP coerente con l'applicazione, ciascuna intestazione presente **una sola volta** (per l'API le imposta l'applicazione, non `web.config`), assenza di `X-Powered-By` e `Server`. L'SP usa il binding Redirect verso l'IdP: non esistono pagine auto-invianti servite da Attestia. L'indice Angular si riconvalida, i file con hash hanno cache lunga e gli asset del portale hanno cache di un giorno. **Esito** ☐

18. **Verificare il limite di caricamento.** Da una sessione HR inviare all'endpoint d'importazione un documento XML fittizio con corpo sopra 2 097 152 byte. **Atteso** — IIS respinge la richiesta per dimensione (normalmente 404.13 oppure 413). Nessun import viene registrato. **Esito** ☐

19. **Verificare i file riservati.** Richiedere `/portale/global.asa`, `/portale/include/auth.asp` e `/portale/web.config`. **Atteso** — HTTP 404, nessun sorgente nella risposta. **Esito** ☐

20. **Verificare identità e ACL.** In Gestione IIS controllare identità, runtime e autenticazione anonima. Controllare `icacls C:\inetpub\attestia\api` e ripetere per `portale` e `admin`. Esaminare anche un file per cartella. **Atteso** — Tre pool a 64 bit con `ApplicationPoolIdentity`: `AttestiaApi` e `AttestiaAdmin` con «Nessun codice gestito», `AttestiaPortale` con CLR v4.0 (le classi .NET Framework `HMACSHA256` e `UTF8Encoding` sono usate via COM da `auth.asp`). L'utente anonimo usa l'identità del pool. Ogni pool dispone di lettura/esecuzione soltanto sulla propria cartella e sui suoi file. **Esito** ☐

21. **Verificare i privilegi effettivi SQL.** In SSMS, come amministratore e nel database Attestia, ispezionare `sys.database_role_members`. Aprire una connessione di prova dedicata ed eseguire le istruzioni seguenti una per volta, compreso `REVERT` dopo il diniego atteso.

    ```sql
    EXECUTE AS USER = N'IIS APPPOOL\AttestiaPortale';
    EXEC dbo.usp_EmployeeCertificateHistory @EmployeeNumber = N'E0001';
    SELECT TOP (1) EmployeeNumber FROM dbo.Employees;
    REVERT;
    ```

    **Atteso** — `AttestiaApi` appartiene a `attestia_api`, `AttestiaPortale` a `attestia_portal`, oltre al ruolo implicito `public`. La procedura è eseguibile dal portale, la lettura diretta viene negata. **Esito** ☐
