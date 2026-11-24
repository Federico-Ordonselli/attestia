import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmployeeHistory } from '../../api/generated/models';
import { EmployeesService } from '../../api/generated/services';
import { ItDatePipe } from '../../shared/it-date.pipe';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';
import { StatusBadge } from '../../shared/status-badge';

@Component({
  selector: 'app-employee-detail-page',
  imports: [RouterLink, StatusBadge, ItDatePipe, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-detail.page.html',
})
export class EmployeeDetailPage {
  private readonly api = inject(EmployeesService);
  /** Parametro di rotta :number */
  readonly number = input.required<string>();
  protected readonly state = new LoadState<EmployeeHistory>();
  protected readonly current = computed(() => (this.state.data()?.certificates ?? []).filter((c) => c.isCurrent));

  constructor() {
    effect(() => {
      const number = this.number();
      untracked(() => void this.state.run(() => this.api.getEmployeeHistory({ number })));
    });
  }
}
