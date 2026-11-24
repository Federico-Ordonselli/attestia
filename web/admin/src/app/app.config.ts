import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { provideApiConfiguration } from './api/generated/api-configuration';
import { authRedirectInterceptor, problemDetailsInterceptor } from './core/interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    // Stessa origine dell'API (un solo sito IIS): URL relativi, così cookie e XSRF funzionano.
    provideApiConfiguration(''),
    provideHttpClient(
      // XSRF integrato di Angular: legge il cookie XSRF-TOKEN (emesso da /api/me) e lo invia in X-XSRF-TOKEN.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      // Ordine: gli errori risalgono la catena al contrario, quindi authRedirect (interno) vede il 401
      // originale prima che problemDetails (esterno) lo trasformi in ApiError.
      withInterceptors([problemDetailsInterceptor, authRedirectInterceptor]),
    ),
  ],
};
