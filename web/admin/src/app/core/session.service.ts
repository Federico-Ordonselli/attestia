import { Injectable, computed, inject, signal } from '@angular/core';
import { MeResponse } from '../api/generated/models';
import { MeService } from '../api/generated/services';

export type Role = 'Employee' | 'HR' | 'Admin';

/**
 * Sessione dell'utente: la verità è il cookie attestia_sso (HttpOnly, non leggibile da JS),
 * quindi l'identità si ottiene da /api/me. Lo stesso /api/me emette il token CSRF.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly me = inject(MeService);
  private readonly current = signal<MeResponse | null>(null);
  private loading: Promise<MeResponse | null> | null = null;

  readonly user = this.current.asReadonly();
  readonly role = computed(() => (this.current()?.role ?? null) as Role | null);
  readonly isHr = computed(() => this.role() === 'HR' || this.role() === 'Admin');
  readonly isAdmin = computed(() => this.role() === 'Admin');

  /** Carica l'utente una sola volta; un 401 viene gestito dall'interceptor (redirect al login SSO). */
  load(): Promise<MeResponse | null> {
    this.loading ??= this.me.getMe().then(
      (user) => {
        this.current.set(user);
        return user;
      },
      () => {
        this.loading = null;
        return null;
      },
    );
    return this.loading;
  }

  hasAnyRole(roles: readonly Role[]): boolean {
    const role = this.role();
    return role !== null && roles.includes(role);
  }
}
