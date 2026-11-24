import { expect, test } from '@playwright/test';
import { expectAccessible, importFile, login } from './helpers';

test('HR vede le scadenze e importa un XML', async ({ page }) => {
  await login(page, 'hr');

  // Riepilogo
  await expect(page.getByRole('heading', { level: 1, name: 'Riepilogo per reparto' })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Attestati per reparto' })).toBeVisible();
  await expectAccessible(page, 'riepilogo');

  // Scadenze: tabella con paginazione lato server
  await page.getByRole('link', { name: 'Scadenze', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Scadenze' })).toBeVisible();
  const table = page.getByRole('table', { name: /Scadenze \(pagina 1 di \d+\)/ });
  await expect(table).toBeVisible();
  await expect(table.getByRole('row')).not.toHaveCount(1); // intestazione + almeno una riga
  await expect(page.getByRole('link', { name: 'Esporta XML' })).toHaveAttribute('href', /\/api\/exports\/expirations\.xml\?withinDays=60/);
  await expectAccessible(page, 'scadenze');

  // Filtro per reparto (URL aggiornato, risultati ricaricati)
  await page.getByLabel('Reparto').selectOption('Logistica');
  await page.getByRole('button', { name: 'Applica filtri' }).click();
  await expect(page).toHaveURL(/department=Logistica/);
  await expect(page.getByRole('table', { name: /Scadenze/ })).toBeVisible();

  // Import: anteprima riga per riga, poi conferma
  await page.getByRole('link', { name: 'Import XML' }).click();
  const file = importFile();
  await page.getByLabel('File XML').setInputFiles(file);
  await page.getByRole('button', { name: 'Anteprima' }).click();
  await expect(page.getByRole('heading', { name: /Anteprima dell'esito/ })).toBeVisible();
  const preview = page.getByRole('table', { name: 'Esito riga per riga' });
  await expect(preview.getByRole('row').filter({ hasText: 'E9999' })).toContainText('Matricola inesistente.');
  await expectAccessible(page, 'anteprima import');

  await page.getByRole('button', { name: /Conferma import di/ }).click();
  await expect(page.getByRole('heading', { name: 'Import completato' })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Ultimi import' })).toContainText(file.name);

  // Lo stesso file una seconda volta viene rifiutato (idempotenza)
  await page.getByLabel('File XML').setInputFiles(file);
  await page.getByRole('button', { name: 'Anteprima' }).click();
  await expect(page.getByRole('alert')).toContainText('già stato importato');
});

test('Admin gestisce i corsi', async ({ page }) => {
  await login(page, 'admin');
  await page.getByRole('link', { name: 'Corsi' }).click();
  await expect(page.getByRole('table', { name: /Corsi/ })).toContainText('GDPR');
  await expectAccessible(page, 'corsi');
});

test('un dipendente non accede al back-office', async ({ page }) => {
  await login(page, 'dipendente');
  await expect(page.getByRole('heading', { name: 'Accesso negato' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Corsi' })).toHaveCount(0);
  const response = await page.request.get('/api/expirations');
  expect(response.status()).toBe(403);
  await expectAccessible(page, 'accesso negato');
});

test('la sessione scaduta o assente riporta al login', async ({ page }) => {
  await page.goto('/admin/scadenze');
  await expect(page).toHaveURL(/\/realms\/attestia\/protocol\/saml/);
});
