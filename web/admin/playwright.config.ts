import { defineConfig, devices } from '@playwright/test';

/**
 * E2E contro lo stack reale di sviluppo: API su https://localhost:7180 (che serve anche /admin dalla build),
 * SQL Server e Keycloak in Docker. Prerequisiti: ./dev.sh up && ./dev.sh db && npm run build && ./dev.sh api
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  fullyParallel: false,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env['ATTESTIA_BASE_URL'] ?? 'https://localhost:7180',
    ignoreHTTPSErrors: true, // certificato di sviluppo di ASP.NET Core
    locale: 'it-IT',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
