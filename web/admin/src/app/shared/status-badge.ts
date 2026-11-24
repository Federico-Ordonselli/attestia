import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CertificateStatus } from '../api/generated/models';

const LABELS: Record<CertificateStatus, { text: string; css: string }> = {
  Valid: { text: 'Valido', css: 'text-bg-success' },
  ExpiringSoon: { text: 'In scadenza', css: 'text-bg-warning' },
  Expired: { text: 'Scaduto', css: 'text-bg-danger' },
  Missing: { text: 'Mancante', css: 'text-bg-secondary' },
};

/** Stato dell'attestato: il testo è sempre presente, il colore è solo un rinforzo (WCAG 1.4.1). */
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="label().css">{{ label().text }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<CertificateStatus>();
  protected readonly label = computed(() => LABELS[this.status()]);
}

export function statusLabel(status: CertificateStatus): string {
  return LABELS[status].text;
}
