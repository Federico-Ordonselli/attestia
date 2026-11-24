import AxeBuilder from '@axe-core/playwright';
import { Page, expect } from '@playwright/test';

export const PASSWORD = 'Dev-Only-Password-1'; // utenti fittizi del realm Keycloak di sviluppo

/** Login SAML reale: /admin/ → 401 → /sso/login → form Keycloak → POST /sso/acs → back-office. */
export async function login(page: Page, username: 'hr' | 'admin' | 'dipendente', path = '/admin/'): Promise<void> {
  await page.goto(path);
  await page.waitForURL(/\/realms\/attestia\//);
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(PASSWORD);
  await page.locator('#kc-login').click();
  await page.waitForURL(/\/(admin|portale)\//);
}

/** Nessuna violazione WCAG 2.0/2.1 livello A e AA rilevabile automaticamente. */
export async function expectAccessible(page: Page, context: string): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const summary = results.violations.map((v) => `${v.id} (${v.impact}): ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(summary, `Violazioni di accessibilità in ${context}`).toEqual([]);
}

/** File di import con exportId univoco: ogni esecuzione produce uno SHA-256 diverso. */
export function importFile(): { name: string; mimeType: string; buffer: Buffer } {
  const now = Date.now();
  const id = `E2E-${now}`;
  // Data della riga valida in un intervallo di ~25 anni (2000-2024): ogni esecuzione usa un giorno diverso,
  // altrimenti la riga risulterebbe "già registrata" (idempotenza) e non ci sarebbe nulla da confermare.
  const date = new Date(Date.UTC(2000, 0, 1) + (Math.floor(now / 1000) % 9000) * 86_400_000).toISOString().slice(0, 10);
  const xml = `<?xml version="1.0" encoding="utf-8"?>
<completions xmlns="urn:attestia:import:v1" exportId="${id}" generatedAt="2026-01-01T00:00:00Z">
  <completion><employeeNumber>E0040</employeeNumber><courseCode>VDT</courseCode><completedOn>${date}</completedOn><score>90</score></completion>
  <completion><employeeNumber>E9999</employeeNumber><courseCode>GDPR</courseCode><completedOn>${date}</completedOn></completion>
</completions>`;
  return { name: `${id}.xml`, mimeType: 'application/xml', buffer: Buffer.from(xml, 'utf-8') };
}
