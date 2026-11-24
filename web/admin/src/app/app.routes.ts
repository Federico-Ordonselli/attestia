import { Routes } from '@angular/router';
import { roleGuard } from './core/role.guard';

export const routes: Routes = [
  { path: '', title: 'Riepilogo · Attestia', canActivate: [roleGuard('HR', 'Admin')], loadComponent: () => import('./pages/dashboard/dashboard.page').then((m) => m.DashboardPage) },
  { path: 'scadenze', title: 'Scadenze · Attestia', canActivate: [roleGuard('HR', 'Admin')], loadComponent: () => import('./pages/expirations/expirations.page').then((m) => m.ExpirationsPage) },
  { path: 'dipendenti', title: 'Dipendenti · Attestia', canActivate: [roleGuard('HR', 'Admin')], loadComponent: () => import('./pages/employees/employees.page').then((m) => m.EmployeesPage) },
  { path: 'dipendenti/:number', title: 'Storico dipendente · Attestia', canActivate: [roleGuard('HR', 'Admin')], loadComponent: () => import('./pages/employees/employee-detail.page').then((m) => m.EmployeeDetailPage) },
  { path: 'import', title: 'Import XML · Attestia', canActivate: [roleGuard('HR', 'Admin')], loadComponent: () => import('./pages/import/import.page').then((m) => m.ImportPage) },
  { path: 'corsi', title: 'Corsi · Attestia', canActivate: [roleGuard('Admin')], loadComponent: () => import('./pages/courses/courses.page').then((m) => m.CoursesPage) },
  { path: 'accesso-negato', title: 'Accesso negato · Attestia', loadComponent: () => import('./pages/misc/forbidden.page').then((m) => m.ForbiddenPage) },
  { path: '**', title: 'Pagina non trovata · Attestia', loadComponent: () => import('./pages/misc/not-found.page').then((m) => m.NotFoundPage) },
];
