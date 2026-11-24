import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role, SessionService } from './session.service';

/** Consente la rotta solo ai ruoli indicati; gli altri vedono la pagina "accesso negato". */
export function roleGuard(...roles: Role[]): CanActivateFn {
  return async () => {
    const session = inject(SessionService);
    const router = inject(Router);
    const user = await session.load();
    if (user === null) {
      return false; // 401: l'interceptor sta già portando l'utente al login
    }
    return session.hasAnyRole(roles) ? true : router.parseUrl('/accesso-negato');
  };
}
