import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { MeService } from '../api/generated/services';
import { EMPLOYEE_USER, HR_USER } from '../../testing/fakes';
import { roleGuard } from './role.guard';
import { SessionService } from './session.service';

describe('roleGuard', () => {
  function setup(getMe: () => Promise<unknown>) {
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: MeService, useValue: { getMe } }] });
    return (roles: Parameters<typeof roleGuard>) => TestBed.runInInjectionContext(() => roleGuard(...roles)({} as never, {} as never)) as Promise<boolean | UrlTree>;
  }

  it('consente HR sulle pagine HR', async () => {
    const run = setup(() => Promise.resolve(HR_USER));
    expect(await run(['HR', 'Admin'])).toBe(true);
  });

  it('manda un Employee alla pagina di accesso negato', async () => {
    const run = setup(() => Promise.resolve(EMPLOYEE_USER));
    const result = await run(['HR', 'Admin']);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/accesso-negato');
  });

  it('HR non accede alle pagine solo Admin', async () => {
    const run = setup(() => Promise.resolve(HR_USER));
    expect(await run(['Admin'])).toBeInstanceOf(UrlTree);
  });

  it('senza sessione blocca la navigazione (il redirect al login lo fa l\'interceptor)', async () => {
    const run = setup(() => Promise.reject(new Error('401')));
    expect(await run(['HR'])).toBe(false);
  });

  it('carica /api/me una sola volta', async () => {
    const getMe = vi.fn(() => Promise.resolve(HR_USER));
    const run = setup(getMe);
    await run(['HR']);
    await run(['HR']);
    expect(getMe).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(SessionService).isHr()).toBe(true);
  });
});
