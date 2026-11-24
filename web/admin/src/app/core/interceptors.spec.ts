import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiError } from './api-error';
import { BrowserLocation, loginUrl } from './browser-location';
import { authRedirectInterceptor, problemDetailsInterceptor } from './interceptors';

describe('interceptor HTTP', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  const location = { assign: vi.fn(), pathAndQuery: '/admin/scadenze?page=2' };

  beforeEach(() => {
    location.assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemDetailsInterceptor, authRedirectInterceptor])), // stesso ordine di app.config.ts
        provideHttpClientTesting(),
        { provide: BrowserLocation, useValue: location },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('su 401 porta al login SSO tornando alla pagina corrente', async () => {
    const request = firstValueFrom(http.get('/api/me'));
    backend.expectOne('/api/me').flush({ title: 'Autenticazione richiesta' }, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeInstanceOf(ApiError);
    expect(location.assign).toHaveBeenCalledWith('/sso/login?returnUrl=%2Fadmin%2Fscadenze%3Fpage%3D2');
  });

  it('trasforma un ProblemDetails in ApiError con messaggio, errori di campo ed estensioni', async () => {
    const request = firstValueFrom(http.post('/api/imports/certificates', {}));
    backend.expectOne('/api/imports/certificates').flush(
      { title: 'File non conforme', detail: 'Nessuna riga salvata.', status: 422, errors: { Code: ['Formato non valido'] }, schemaErrors: [{ line: 3 }] },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    const error = (await request.catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(422);
    expect(error.userMessage).toBe('Nessuna riga salvata.');
    expect(error.fieldErrors['Code']).toEqual(['Formato non valido']);
    expect(error.extensions['schemaErrors']).toEqual([{ line: 3 }]);
    expect(location.assign).not.toHaveBeenCalled();
  });

  it('errore di rete: messaggio comprensibile', async () => {
    const request = firstValueFrom(http.get('/api/courses'));
    backend.expectOne('/api/courses').error(new ProgressEvent('error'), { status: 0 });

    const error = (await request.catch((e: unknown) => e)) as ApiError;
    expect(error.userMessage).toContain('Impossibile contattare il server');
  });

  it('il returnUrl accetta solo percorsi locali', () => {
    expect(loginUrl('https://evil.example.test/')).toBe('/sso/login?returnUrl=%2Fadmin%2F');
    expect(loginUrl('//evil.example.test/')).toBe('/sso/login?returnUrl=%2Fadmin%2F');
  });
});
