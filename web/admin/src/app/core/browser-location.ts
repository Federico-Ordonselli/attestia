import { Injectable } from '@angular/core';

/** Navigazioni "a pagina intera" (login/logout SSO): isolate qui per poterle sostituire nei test. */
@Injectable({ providedIn: 'root' })
export class BrowserLocation {
  assign(url: string): void {
    window.location.assign(url);
  }

  get pathAndQuery(): string {
    return window.location.pathname + window.location.search;
  }
}

/** Solo percorsi locali: mai URL assoluti o protocol-relative (open redirect). */
export function loginUrl(returnUrl: string): string {
  const safe = returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/admin/';
  return `/sso/login?returnUrl=${encodeURIComponent(safe)}`;
}
