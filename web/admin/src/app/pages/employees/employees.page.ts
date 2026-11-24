import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Employee } from '../../api/generated/models';
import { EmployeesService } from '../../api/generated/services';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';

@Component({
  selector: 'app-employees-page',
  imports: [RouterLink, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employees.page.html',
})
export class EmployeesPage {
  private readonly api = inject(EmployeesService);
  protected readonly departments = signal<string[]>([]);
  protected readonly state = new LoadState<Employee[]>();
  protected readonly searched = signal(false);

  constructor() {
    void this.api.listDepartments().then((d) => this.departments.set(d), () => this.departments.set([]));
  }

  protected search(query: string, department: string): void {
    this.searched.set(true);
    void this.state.run(() => this.api.searchEmployees({ query: query.trim() || undefined, department: department || undefined }));
  }
}
