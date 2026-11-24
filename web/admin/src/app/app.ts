import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SessionService } from './core/session.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
})
export class App {
  protected readonly session = inject(SessionService);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    // Dopo ogni cambio di pagina il focus va al contenuto principale (annunciato dagli screen reader).
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => queueMicrotask(() => this.main().nativeElement.focus()));
  }
}
