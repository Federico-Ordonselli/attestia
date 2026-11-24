import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ApiError } from './api-error';
import { BrowserLocation, loginUrl } from './browser-location';

/** 401 = sessione SSO assente o scaduta: si riparte dal login SAML, tornando alla pagina corrente. */
export const authRedirectInterceptor: HttpInterceptorFn = (req, next) => {
  const location = inject(BrowserLocation);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        location.assign(loginUrl(location.pathAndQuery));
      }
      return throwError(() => error);
    }),
  );
};

/** Ogni errore HTTP diventa un ApiError con titolo/dettaglio del ProblemDetails. */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) =>
      throwError(() => (error instanceof HttpErrorResponse ? ApiError.fromHttp(error) : error)),
    ),
  );
