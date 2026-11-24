import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Indicatore di caricamento annunciato agli screen reader. */
@Component({
  selector: 'app-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="d-flex align-items-center gap-2 my-3" role="status">
      <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
      <span>{{ label() }}</span>
    </div>`,
})
export class Loading {
  readonly label = input('Caricamento in corso…');
}

@Component({
  selector: 'app-error-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="alert alert-danger my-3" role="alert"><strong>Errore.</strong> {{ message() }}</div>`,
})
export class ErrorPanel {
  readonly message = input.required<string>();
}

@Component({
  selector: 'app-empty',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="alert alert-light border my-3 mb-0">{{ message() }}</p>`,
})
export class Empty {
  readonly message = input.required<string>();
}
