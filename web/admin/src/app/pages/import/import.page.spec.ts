import { TestBed } from '@angular/core/testing';
import { ImportReport } from '../../api/generated/models';
import { ImportsService } from '../../api/generated/services';
import { ApiError } from '../../core/api-error';
import { settle } from '../../../testing/fakes';
import { ImportPage } from './import.page';

describe('ImportPage', () => {
  const report = (dryRun: boolean): ImportReport => ({
    dryRun, fileName: 'lms.xml', sha256: 'a'.repeat(64), totalRows: 2, okRows: 1, rejectedRows: 1, importId: dryRun ? null : 7,
    rows: [
      { lineNumber: 4, employeeNumber: 'E0010', courseCode: 'GDPR', completedOn: '2026-11-12', outcome: 'Imported', expiresOn: '2028-11-12', status: 'Valid' },
      { lineNumber: 10, employeeNumber: 'E9999', courseCode: 'GDPR', completedOn: '2026-11-12', outcome: 'Rejected', reason: 'Matricola inesistente.' },
    ],
  });
  let api: { importCertificates: ReturnType<typeof vi.fn>; listImports: ReturnType<typeof vi.fn> };

  async function render() {
    api = { importCertificates: vi.fn(), listImports: vi.fn().mockResolvedValue([]) };
    TestBed.configureTestingModule({ imports: [ImportPage], providers: [{ provide: ImportsService, useValue: api }] });
    const fixture = TestBed.createComponent(ImportPage);
    await settle(fixture);
    return fixture;
  }

  function choose(fixture: Awaited<ReturnType<typeof render>>, file: File) {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: { item: () => file, length: 1 } });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const button = (fixture: Awaited<ReturnType<typeof render>>, text: string) =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes(text));

  it('anteprima (dryRun) → esito riga per riga → conferma (salvataggio) → storico aggiornato', async () => {
    const fixture = await render();
    const file = new File(['<completions/>'], 'lms.xml', { type: 'application/xml' });
    choose(fixture, file);

    api.importCertificates.mockResolvedValueOnce(report(true));
    button(fixture, 'Anteprima')!.click();
    await settle(fixture);
    const html = fixture.nativeElement as HTMLElement;

    expect(api.importCertificates).toHaveBeenLastCalledWith({ dryRun: true, body: { file } });
    expect(html.textContent).toContain('Anteprima dell\'esito');
    expect(html.textContent).toContain('Matricola inesistente.');
    expect(html.querySelectorAll('tr.table-danger')).toHaveLength(1);

    api.importCertificates.mockResolvedValueOnce(report(false));
    button(fixture, 'Conferma import di 1 riga')!.click();
    await settle(fixture);

    expect(api.importCertificates).toHaveBeenLastCalledWith({ dryRun: false, body: { file } });
    expect(html.textContent).toContain('Import completato');
    expect(html.textContent).toContain('Import n. 7');
    expect(api.listImports).toHaveBeenCalledTimes(2);
    expect(button(fixture, 'Conferma import')).toBeUndefined();
  });

  it('mostra gli errori di schema con riga e colonna', async () => {
    const fixture = await render();
    choose(fixture, new File(['x'], 'ko.xml'));
    api.importCertificates.mockRejectedValueOnce(new ApiError(422, 'File non conforme allo schema', 'Nessuna riga è stata salvata.', {}, {
      schemaErrors: [{ line: 12, position: 5, message: "'12/11/2026' is not a valid value" }],
    }));

    button(fixture, 'Anteprima')!.click();
    await settle(fixture);
    const html = fixture.nativeElement as HTMLElement;

    expect(html.querySelector('[role=alert]')!.textContent).toContain('Nessuna riga è stata salvata.');
    expect(html.textContent).toContain('Errori di schema (1)');
    expect(html.textContent).toContain('12/11/2026');
  });

  it('rifiuta lato client i file oltre 2 MB senza chiamare l\'API', async () => {
    const fixture = await render();
    choose(fixture, new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'grande.xml'));

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('supera la dimensione massima');
    expect(button(fixture, 'Anteprima')!.disabled).toBe(true);
    expect(api.importCertificates).not.toHaveBeenCalled();
  });
});
