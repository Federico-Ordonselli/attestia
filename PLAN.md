# Piano di lavoro

Stato: ✅ completata · 🔄 in corso · ⏳ da fare

| # | Fase | Stato | Note |
|---|------|-------|------|
| 1 | Fondamenta: struttura repo, CLAUDE/DECISIONS/PLAN, docker-compose (MSSQL + Keycloak), runner migrazioni | ✅ | runner DbUp idempotente verificato su MSSQL 2022 in Docker |
| 2 | Database: schema, tabella temporale, stored procedure, seed, test di integrazione | ✅ | 31 test di integrazione su MSSQL 2022 (Testcontainers) |
| 3 | Dominio e API: stato/scadenza, CRUD, scadenze, riepilogo, autorizzazione, OpenAPI | ✅ | 121 test (unit + parità T-SQL/C# + endpoint + autorizzazione) |
| 4 | Import/export XML: XSD, parser sicuro, import idempotente, export | ✅ | 160 test; anti-XXE, concorrenza, export validato contro XSD |
| 5 | SAML + SSO: Service Provider, Keycloak, cookie firmato, `/api/me` | ✅ | 207 test; login e logout SAML verificati anche con Keycloak reale |
| 6 | Angular back-office: schermate, client generato, test, e2e | ✅ | 25 test Vitest, 4 e2e Playwright (SAML reale + axe), lint pulito |
| 7 | Portale ASP classic: pagine, include, JSON, assets; controlli statici | ✅ | controlli statici superati (anche con test di mutazione); esecuzione **da verificare su Windows** |
| 8 | IIS e documentazione: PowerShell, web.config, checklist, README | ✅ | script analizzato (parser + PSScriptAnalyzer in Docker), pacchetto win-x64 costruito; esecuzione **da verificare su Windows** |
| 9 | CI e rifinitura: GitHub Actions, audit, pulizia | ⏳ | |

## Verificato in automatico (Linux) / Da verificare su Windows

Aggiornata a ogni fase; la stessa tabella è nel README.

| Area | Verificato in automatico (Linux) | Da verificare su Windows |
|---|---|---|
| Database | Schema, vincoli, tabella temporale, stored procedure, seed, permessi dei ruoli: test di integrazione su SQL Server 2022 reale | Login `IIS APPPOOL\…` associati ai ruoli (CHECKLIST 21) |
| Regole di stato | Stessi casi eseguiti su T-SQL e C# (test di parità) | — |
| API | Endpoint, validazione, ProblemDetails, autorizzazione per ruolo, CSRF, rate limit, intestazioni di sicurezza | Hosting in-process sotto IIS (ANCM), persistenza chiavi Data Protection |
| Import/export XML | XSD, anti-XXE, limiti, report per riga, idempotenza, concorrenza, export validato contro XSD; esempi verificati anche con `xmllint` | — |
| SAML / SSO | Test con IdP firmante in memoria (firma, audience, scadenza, InResponseTo, rotazione chiavi); login e logout con **Keycloak reale** (curl ed e2e) | Keycloak raggiunto dall'origine `attestia.local` (CHECKLIST 3, 7–8) |
| Back-office Angular | Test unitari, lint, build, e2e Playwright con login SAML reale e controlli axe WCAG 2.1 AA su 5 schermate | Deep link sotto IIS con URL Rewrite (CHECKLIST 15) |
| Portale ASP classic | Controlli statici: UTF-8/CRLF, `Option Explicit`, niente SQL concatenato, parametri e colonne coerenti con le procedure, output codificato, contratto del cookie | **Tutta l'esecuzione**: VBScript, ADO/MSOLEDBSQL, HMAC via COM (autotest col vettore, CHECKLIST 11), cookie alterati o scaduti, JSON (CHECKLIST 9–14) |
| IIS | `web.config` XML ben formati; `Install-Attestia.ps1` analizzato con il parser PowerShell e PSScriptAnalyzer (in Docker); pacchetto `win-x64` costruito | **Esecuzione dello script**, pool, ACL, rewrite, intestazioni, limiti di upload, file riservati (CHECKLIST 4–6, 17–20) |
| CI | Workflow GitHub Actions scritti e validati con `actionlint` | Esecuzione su GitHub (il repository non è ancora pubblicato) |
