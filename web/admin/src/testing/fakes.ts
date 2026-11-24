import { MeResponse } from '../app/api/generated/models';

/** Utenti fittizi per i test dei componenti. */
export const HR_USER: MeResponse = { employeeNumber: 'E0002', role: 'HR', displayName: 'Ettore Calcagnetti', department: 'Amministrazione' };
export const EMPLOYEE_USER: MeResponse = { employeeNumber: 'E0001', role: 'Employee', displayName: 'Nadia Verdolino', department: 'Produzione' };
export const ADMIN_USER: MeResponse = { employeeNumber: 'E0003', role: 'Admin', displayName: 'Livia Sbaraglini', department: 'IT' };

/** Attende che le promise e il change detection zoneless si stabilizzino. */
export async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  for (let i = 0; i < 3; i++) {
    await new Promise((r) => setTimeout(r));
    await fixture.whenStable();
    fixture.detectChanges();
  }
}
