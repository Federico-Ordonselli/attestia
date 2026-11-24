import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { EmployeesService, ReportsService } from '../../api/generated/services';
import { settle } from '../../../testing/fakes';
import { ExpirationsPage, PAGE_SIZE } from './expirations.page';

describe('ExpirationsPage', () => {
  const row = (i: number) => ({
    certificateId: i, employeeNumber: `E00${i}`, firstName: 'Nome', lastName: `Cognome${i}`, department: 'Logistica',
    courseCode: 'GDPR', courseTitle: 'Privacy', completedOn: '2024-12-10', expiresOn: '2026-12-10', daysToExpiry: 16, status: 'ExpiringSoon' as const,
  });
  let listExpirations: ReturnType<typeof vi.fn>;

  async function render(query: Record<string, string> = {}) {
    listExpirations = vi.fn().mockResolvedValue({ items: [row(1), row(2)], page: 1, pageSize: PAGE_SIZE, totalCount: 45 });
    TestBed.configureTestingModule({
      imports: [ExpirationsPage],
      providers: [
        provideRouter([]),
        { provide: ReportsService, useValue: { listExpirations } },
        { provide: EmployeesService, useValue: { listDepartments: () => Promise.resolve(['Logistica', 'IT']) } },
      ],
    });
    const fixture = TestBed.createComponent(ExpirationsPage);
    for (const [k, v] of Object.entries(query)) {
      fixture.componentRef.setInput(k, v);
    }
    await settle(fixture);
    return fixture;
  }

  it('carica la prima pagina con i filtri di default e mostra badge e date in italiano', async () => {
    const fixture = await render();
    const text = (fixture.nativeElement as HTMLElement).textContent!;

    expect(listExpirations).toHaveBeenCalledWith({ withinDays: 60, department: undefined, page: 1, pageSize: PAGE_SIZE });
    expect(text).toContain('45 attestati trovati');
    expect(text).toContain('10/12/2026');
    expect(text).toContain('In scadenza');
    expect(text).toContain('Pagina 1 di 3');
  });

  it('usa i parametri di query (paginazione lato server) e aggiorna il link di export', async () => {
    const fixture = await render({ withinDays: '30', department: 'IT', page: '2' });
    const exportLink = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a[download]')!;

    expect(listExpirations).toHaveBeenCalledWith({ withinDays: 30, department: 'IT', page: 2, pageSize: PAGE_SIZE });
    expect(exportLink.getAttribute('href')).toBe('/api/exports/expirations.xml?withinDays=30&department=IT');
  });

  it('"Successiva" naviga alla pagina seguente mantenendo i filtri', async () => {
    const fixture = await render({ department: 'IT' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const next = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes('Successiva'))!;

    next.click();

    expect(navigate).toHaveBeenCalledWith([], { queryParams: { withinDays: 60, department: 'IT', page: 2 } });
  });

  it('valori di query non validi vengono riportati nei limiti', async () => {
    await render({ withinDays: '-5', page: 'abc' });
    expect(listExpirations).toHaveBeenCalledWith({ withinDays: 0, department: undefined, page: 1, pageSize: PAGE_SIZE });
  });
});
