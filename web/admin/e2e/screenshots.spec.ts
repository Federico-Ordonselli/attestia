import { expect, test } from '@playwright/test';
import { importFile, login } from './helpers';

// Genera gli screenshot della documentazione (docs/screenshots). Solo su richiesta: SCREENSHOTS=1 npx playwright test screenshots
test.skip(!process.env['SCREENSHOTS'], 'impostare SCREENSHOTS=1 per rigenerare gli screenshot');
test.use({ viewport: { width: 1360, height: 900 } });

const out = (name: string) => `../../docs/screenshots/${name}.png`;

test('screenshot del back-office', async ({ page }) => {
  await login(page, 'admin');
  await expect(page.getByRole('table', { name: 'Attestati per reparto' })).toBeVisible();
  await page.screenshot({ path: out('admin-riepilogo'), fullPage: true });

  await page.goto('/admin/scadenze?withinDays=90');
  await expect(page.getByRole('table', { name: /Scadenze/ })).toBeVisible();
  await page.screenshot({ path: out('admin-scadenze'), fullPage: true });

  await page.goto('/admin/import');
  await page.getByLabel('File XML').setInputFiles(importFile());
  await page.getByRole('button', { name: 'Anteprima' }).click();
  await expect(page.getByRole('table', { name: 'Esito riga per riga' })).toBeVisible();
  await page.screenshot({ path: out('admin-import-anteprima'), fullPage: true });

  await page.goto('/admin/corsi');
  await expect(page.getByRole('table', { name: /Corsi/ })).toBeVisible();
  await page.screenshot({ path: out('admin-corsi'), fullPage: true });

  await page.goto('/admin/dipendenti/E0001');
  await expect(page.getByRole('table', { name: /Storico attestati/ })).toBeVisible();
  await page.screenshot({ path: out('admin-storico-dipendente'), fullPage: true });
});
