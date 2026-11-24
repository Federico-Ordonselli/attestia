import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DepartmentSummary } from '../../api/generated/models';
import { ReportsService } from '../../api/generated/services';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.page.html',
})
export class DashboardPage {
  private readonly reports = inject(ReportsService);
  protected readonly state = new LoadState<DepartmentSummary[]>();

  protected readonly totals = computed(() =>
    (this.state.data() ?? []).reduce(
      (t, d) => ({
        employees: t.employees + d.employeeCount,
        valid: t.valid + d.validCount,
        expiring: t.expiring + d.expiringSoonCount,
        expired: t.expired + d.expiredCount,
        missing: t.missing + d.missingCount,
      }),
      { employees: 0, valid: 0, expiring: 0, expired: 0, missing: 0 },
    ),
  );

  constructor() {
    void this.load();
  }

  protected load(): Promise<unknown> {
    return this.state.run(() => this.reports.getDepartmentSummary());
  }
}
