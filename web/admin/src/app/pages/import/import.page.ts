import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ImportLogResponse, ImportReport } from '../../api/generated/models';
import { ImportsService } from '../../api/generated/services';
import { ApiError, errorMessage } from '../../core/api-error';
import { ItDatePipe } from '../../shared/it-date.pipe';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';
import { StatusBadge } from '../../shared/status-badge';

export interface SchemaError {
  line: number;
  position: number;
  message: string;
}

const MAX_BYTES = 2 * 1024 * 1024;

/**
 * Import in due passi: 1) anteprima (dryRun, nulla viene salvato) con l'esito riga per riga;
 * 2) conferma, che salva in un'unica transazione le sole righe valide.
 */
@Component({
  selector: 'app-import-page',
  imports: [StatusBadge, ItDatePipe, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './import.page.html',
})
export class ImportPage {
  private readonly api = inject(ImportsService);
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly report = signal<ImportReport | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly schemaErrors = signal<SchemaError[]>([]);
  protected readonly history = new LoadState<ImportLogResponse[]>();
  protected readonly canConfirm = computed(() => this.report()?.dryRun === true && this.report()!.okRows > 0 && !this.busy());

  constructor() {
    void this.loadHistory();
  }

  protected loadHistory(): Promise<unknown> {
    return this.history.run(() => this.api.listImports());
  }

  protected select(input: HTMLInputElement): void {
    const file = input.files?.item(0) ?? null;
    this.reset();
    if (file && file.size > MAX_BYTES) {
      this.error.set('Il file supera la dimensione massima di 2 MB.');
      this.file.set(null);
      return;
    }
    this.file.set(file);
  }

  protected preview(): Promise<void> {
    return this.send(true);
  }

  protected async confirm(): Promise<void> {
    await this.send(false);
    if (this.report()?.dryRun === false) {
      await this.loadHistory();
    }
  }

  private async send(dryRun: boolean): Promise<void> {
    const file = this.file();
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.schemaErrors.set([]);
    try {
      this.report.set(await this.api.importCertificates({ dryRun, body: { file } }));
    } catch (e) {
      this.report.set(null);
      this.error.set(errorMessage(e));
      if (e instanceof ApiError && Array.isArray(e.extensions['schemaErrors'])) {
        this.schemaErrors.set(e.extensions['schemaErrors'] as SchemaError[]);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private reset(): void {
    this.report.set(null);
    this.error.set(null);
    this.schemaErrors.set([]);
  }
}
