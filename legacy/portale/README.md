# Portale dipendenti Attestia

**Stato del collaudo: da verificare su Windows.** Il portale mostra lo storico della matricola autenticata, anche quando chi accede ha ruolo HR o Admin. Il riepilogo conta l'ultima situazione di ogni corso, comprese le formazioni obbligatorie mancanti. La tabella conserva anche le righe precedenti, contrassegnate con «Rinnovato».

## Struttura

| Percorso | Funzione |
| --- | --- |
| `default.asp` | Saluto, riepilogo e tabella HTML disponibili anche senza JavaScript. |
| `certificates-json.asp` | Aggiornamento dello stesso storico. Restituisce 401 JSON quando occorre accedere di nuovo. |
| `denied.asp`, `logout.asp`, `error.html` | Accesso negato, uscita SSO e messaggio generico per gli errori del server. |
| `include/auth.asp` | Lettura, verifica e scadenza del cookie. |
| `include/db.asp` | Comando ADO tipizzato per `dbo.usp_EmployeeCertificateHistory` e cursore client disconnesso. |
| `include/helpers.asp` | Codifica HTML e JSON, date, etichette e orologio UTC. |
| `global.asa` | Inizializzazione del titolo dell'applicazione. |
| `assets/css`, `assets/js` | Contrasto e focus, ricerca, ordinamento delle date e aggiornamento della tabella. |
| `assets/vendor` | Copie locali di Bootstrap 3.4.1, jQuery 3.7.1 e DataTables 3.1.6 con integrazione Bootstrap 3. |

Bootstrap 3 è fuori supporto. Il suo impiego è intenzionale per rappresentare il mantenimento di un'applicazione preesistente. In produzione si programmerebbe la migrazione a una versione supportata.

## Configurazione del pool

| Variabile di processo | Contenuto |
| --- | --- |
| `ATTESTIA_PORTAL_DB` | Stringa ADO, ad esempio `Provider=MSOLEDBSQL19;Server=.;Database=Attestia;Integrated Security=SSPI;DataTypeCompatibility=80;`. Il login del pool appartiene al solo ruolo applicativo `attestia_portal`. |
| `ATTESTIA_SSO_KEY` | Chiave casuale in base64 condivisa con il pool API. La decodifica deve produrre almeno 32 byte. |

Una connessione mancante genera un errore generico. Una chiave assente, non valida o un errore COM negano l'accesso. Per ruotare la chiave aggiornare entrambi i pool, riciclarli e ripetere l'accesso.

## Contratto SSO

L'API convalida l'asserzione SAML e imposta `attestia_sso` con `Path=/`, `HttpOnly`, `Secure` e `SameSite=Lax`. Il valore ha esattamente cinque campi.

```text
v1|matricola|ruolo|scadenza|firma
```

La matricola soddisfa `^[A-Z0-9-]{2,20}$`. I ruoli ammessi sono `Employee`, `HR` e `Admin`. La scadenza contiene da una a dieci cifre decimali ed esprime i secondi Unix UTC. Il cookie scade nell'istante indicato. I soli caratteri ammessi nel valore sono lettere ASCII, cifre, trattino e barra verticale.

La firma è l'HMAC-SHA256 dei byte UTF-8 dei primi quattro campi con i tre separatori originali. La chiave è il contenuto binario decodificato dal base64. La verifica confronta tutti i 64 caratteri esadecimali minuscoli. La matricola utilizzata per la procedura proviene esclusivamente dal cookie verificato.

## Pubblicazione e collaudo

Dalla radice del progetto eseguire `python3 scripts/check_portal.py` e `deploy/iis/build-package.sh`. Distribuire `deploy/iis/out/portale` come applicazione figlia `/portale`. Ogni pagina `.asp` inizia con la direttiva `<%@ ... %>` seguita da `Option Explicit`; i file in `include/` non le ripetono (vengono copiati testualmente nella pagina, e una seconda direttiva causerebbe l'errore ASP 0141). I sorgenti sono UTF-8 senza BOM con terminazioni CRLF e vengono distribuiti così come sono, senza trasformazioni.

Per verificare su Windows l'HMAC calcolato in VBScript usare `../portale-tests/hmac-selftest.asp` (passo dedicato nella checklist): confronta il risultato con il vettore di test calcolato in Python.

Il controllo Linux legge i contratti SQL dalle migrazioni. Firma interoperabile, componenti COM, connessione con l'identità del pool e risposte HTTP richiedono il collaudo descritto nella [lista di verifica IIS](../../deploy/iis/CHECKLIST.md).
