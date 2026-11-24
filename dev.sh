#!/usr/bin/env bash
# Comandi di sviluppo di Attestia (Linux). Uso: ./dev.sh <comando>
#   up        avvia SQL Server 2022 e Keycloak (Docker Compose) e attende che siano pronti
#   down      ferma i container (i dati restano nel volume)
#   db        applica migrazioni + seed (un solo comando, idempotente)
#   api       avvia l'API su https://localhost:7180 (serve anche /admin se e' stato compilato)
#   admin     compila il back-office Angular in modalita' watch (servito dall'API sotto /admin)
#   test      test .NET (unit + integrazione con Testcontainers) e test Angular
#   e2e       test end-to-end Playwright (richiede up + db + api in esecuzione)
#   openapi   rigenera contracts/openapi e il client Angular tipizzato
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]]; then
  echo "Manca .env: eseguo 'cp .env.example .env' (valori di solo sviluppo)."
  cp .env.example .env
fi
set -a; . ./.env; set +a

COMPOSE=(docker compose --env-file .env -f deploy/docker/docker-compose.yml)

case "${1:-help}" in
  up)      "${COMPOSE[@]}" up -d --wait ;;
  down)    "${COMPOSE[@]}" down ;;
  db)      dotnet run --project src/Attestia.Api --no-launch-profile -- migrate --seed ;;
  api)     ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Attestia.Api --launch-profile https ;;
  admin)   (cd web/admin && npm run build -- --watch) ;;
  test)    dotnet test && (cd web/admin && npm test) ;;
  e2e)     (cd web/admin && npm run build && npx playwright test) ;;
  openapi) dotnet build src/Attestia.Api && (cd web/admin && npm run gen:api) ;;
  *)       sed -n '2,11p' "$0" ;;
esac
