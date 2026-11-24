import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PagedResultOfExpiringCertificate } from '../../api/generated/models';
import { EmployeesService, ReportsService } from '../../api/generated/services';
import { ItDatePipe } from '../../shared/it-date.pipe';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';
import { StatusBadge } from '../../shared/status-badge';

export const PAGE_SIZE = 20;

/** Filtri e pagina sono nell'URL (?withinDays=&department=&page=): la vista è condivisibile e il "indietro" funziona. */
@Component({
  selector: 'app-expirations-page',
  imports: [FormsModule, StatusBadge, ItDatePipe, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './expirations.page.html',
})
export class ExpirationsPage {
  private readonly reports = inject(ReportsService);
  private readonly employees = inject(EmployeesService);
  private readonly router = inject(Router);

  // Parametri di query (withComponentInputBinding)
  readonly withinDays = input<string | undefined>();
  readonly department = input<string | undefined>();
  readonly page = input<string | undefined>();

  protected readonly horizonOptions = [0, 30, 60, 90, 180, 365];
  protected readonly departments = signal<string[]>([]);
  protected readonly state = new LoadState<PagedResultOfExpiringCertificate>();

  protected readonly filters = computed(() => ({
    withinDays: clampInt(this.withinDays(), 60, 0, 3650),
    department: this.department() || '',
    page: clampInt(this.page(), 1, 1, 100_000),
  }));

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil((this.state.data()?.totalCount ?? 0) / PAGE_SIZE)));

  protected readonly exportUrl = computed(() => {
    const f = this.filters();
    const q = new URLSearchParams({ withinDays: String(f.withinDays) });
    if (f.department) {
      q.set('department', f.department);
    }
    return `/api/exports/expirations.xml?${q.toString()}`;
  });

  constructor() {
    void this.employees.listDepartments().then((d) => this.departments.set(d), () => this.departments.set([]));
    effect(() => {
      const f = this.filters();
      untracked(() => void this.state.run(() =>
        this.reports.listExpirations({ withinDays: f.withinDays, department: f.department || undefined, page: f.page, pageSize: PAGE_SIZE })));
    });
  }

  protected applyFilters(withinDays: number, department: string): void {
    void this.navigate({ withinDays, department, page: 1 });
  }

  protected goToPage(page: number): void {
    void this.navigate({ ...this.filters(), page });
  }

  private navigate(f: { withinDays: number; department: string; page: number }): Promise<boolean> {
    return this.router.navigate([], {
      queryParams: { withinDays: f.withinDays, department: f.department || null, page: f.page > 1 ? f.page : null },
    });
  }
}

function clampInt(raw: string | undefined, fallback: number, min: number, max: number): number {
  const n = Number.parseInt(raw ?? '', 10);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}
