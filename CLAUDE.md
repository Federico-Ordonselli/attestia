# CLAUDE.md — Attestia

Registro della formazione obbligatoria con scadenze. Progetto portfolio: .NET 10 (C#), Angular, SQL Server, ASP classic, IIS, SAML 2.0.

## Comandi

```bash
cp .env.example .env          # solo la prima volta (valori di sviluppo finti)
./dev.sh up                   # SQL Server 2022 + Keycloak in Docker (attende healthcheck)
./dev.sh db                   # migrazioni + seed (idempotente) = dotnet run --project src/Attestia.Api -- migrate --seed
./dev.sh api                  # API su https://localhost:7180 (serve /admin se web/admin e' compilato)
./dev.sh admin                # build Angular in watch -> web/admin/dist
dotnet build                  # warning = errori (TreatWarningsAsErrors + analyzers latest-recommended)
dotnet test                   # unit + integrazione (Testcontainers avvia un SQL Server usa-e-getta: serve Docker)
dotnet test --filter "Category!=Integration"   # solo unit test, senza Docker
cd web/admin && npm test      # test Angular (Vitest)
cd web/admin && npm run e2e   # Playwright + axe (richiede up, db, api)
./dev.sh openapi              # rigenera contracts/openapi/attestia-api.json e il client Angular
```

## Struttura

- `src/Attestia.Core` — dominio puro, senza I/O: regole di stato/scadenza, regole di import per riga, parser XML sicuro, formato del cookie SSO. Gli XSD di `/contracts` sono risorse incorporate.
- `src/Attestia.Data` — Dapper + stored procedure (nessun SQL ad hoc oltre alle chiamate `EXEC`), runner DbUp. Gli script di `/db/migrations` e `/db/seed` sono incorporati.
- `src/Attestia.Api` — minimal API (`/api/*`), Service Provider SAML (`/sso/*`), autenticazione tramite cookie `attestia_sso`.
- `tests/Attestia.Tests` — xUnit v3. `Category=Integration` usa Testcontainers (MSSQL reale).
- `web/admin` — Angular (standalone, signals, strict), Bootstrap 5, servito sotto `/admin`.
- `legacy/portale` — ASP classic (VBScript), jQuery + Bootstrap 3 locali. Non eseguibile su Linux.
- `db/migrations` (`V###__*.sql`, journal `SchemaVersions`), `db/seed` (`S###__*.sql`, journal `SeedVersions`).
- `contracts` — XSD import/export, esempi XML, OpenAPI generato.
- `deploy/docker` — compose + realm Keycloak (generato da `scripts/build-realm.py`).
- `deploy/iis` — script PowerShell, web.config, checklist Windows.

## Convenzioni

- Identificatori (codice, tabelle, colonne, endpoint, file) in **inglese**; UI, README e documentazione in **italiano**.
- Una migrazione non si modifica mai dopo il commit: si aggiunge `V00N+1`. Le procedure usano `CREATE OR ALTER`.
- La logica di stato vive in `dbo.fn_CertificateStatus` (T-SQL) e in `CertificateStatusRules` (C#): se cambi una, cambia l'altra e il test di parita' `StatusParityTests`.
- Logging con `[LoggerMessage]` (CA1848). Mai loggare contenuto di file importati, cookie, asserzioni SAML.
- Date di calendario = `DATE` / `DateOnly`, "oggi" nel fuso Europe/Rome (`IBusinessClock`).
- Errori API sempre in `ProblemDetails`; nessun dettaglio di eccezione nelle risposte.
- Commit in Conventional Commits, uno per fase, con test verdi.
- Dati di esempio solo fittizi, email `@example.test`.
- Segreti mai nel repository: `.env` e' ignorato, `.env.example` contiene solo valori di sviluppo finti.

## Insidie note

- `sqlcmd` ha `QUOTED_IDENTIFIER OFF` di default: usare `-I` (le colonne calcolate persistite lo richiedono). SqlClient/DbUp lo hanno gia' ON.
- Su Arch/CachyOS servono `dotnet-sdk-10.0`, `aspnet-runtime-10.0` **e** `aspnet-targeting-pack-10.0` (altrimenti NETSDK1226).
- La porta 8080 e' occupata sulla macchina di sviluppo: Keycloak usa 8081.
- La generazione OpenAPI a build-time avvia l'host: niente accessi a DB o IdP durante la costruzione dei servizi.
