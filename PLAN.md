# Piano di lavoro

Stato: ✅ completata · 🔄 in corso · ⏳ da fare

| # | Fase | Stato | Note |
|---|------|-------|------|
| 1 | Fondamenta: struttura repo, CLAUDE/DECISIONS/PLAN, docker-compose (MSSQL + Keycloak), runner migrazioni | ✅ | runner DbUp idempotente verificato su MSSQL 2022 in Docker |
| 2 | Database: schema, tabella temporale, stored procedure, seed, test di integrazione | ✅ | 31 test di integrazione su MSSQL 2022 (Testcontainers) |
| 3 | Dominio e API: stato/scadenza, CRUD, scadenze, riepilogo, autorizzazione, OpenAPI | ✅ | 121 test (unit + parità T-SQL/C# + endpoint + autorizzazione) |
| 4 | Import/export XML: XSD, parser sicuro, import idempotente, export | ⏳ | |
| 5 | SAML + SSO: Service Provider, Keycloak, cookie firmato, `/api/me` | ⏳ | |
| 6 | Angular back-office: schermate, client generato, test, e2e | ⏳ | |
| 7 | Portale ASP classic: pagine, include, JSON, assets; controlli statici | ⏳ | |
| 8 | IIS e documentazione: PowerShell, web.config, checklist, README | ⏳ | |
| 9 | CI e rifinitura: GitHub Actions, audit, pulizia | ⏳ | |

## Verificato in automatico (Linux) / Da verificare su Windows

| Area | Linux (automatico) | Windows (manuale) |
|------|--------------------|-------------------|
| _verrà compilata man mano_ | | |
