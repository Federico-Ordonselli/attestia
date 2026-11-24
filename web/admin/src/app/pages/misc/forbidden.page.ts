import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-forbidden-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="h2">Accesso negato</h1>
    <p>Il back-office è riservato al personale HR. Puoi consultare i tuoi attestati nel
      <a href="/portale/">portale dipendenti</a>.</p>
    <p><a href="/sso/logout">Esci</a></p>`,
})
export class ForbiddenPage {}
