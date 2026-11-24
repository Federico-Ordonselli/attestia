import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="h2">Pagina non trovata</h1><p><a routerLink="/">Torna al riepilogo</a></p>`,
})
export class NotFoundPage {}
