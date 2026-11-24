import { signal } from '@angular/core';
import { errorMessage } from '../core/api-error';

/**
 * Stato di una richiesta asincrona come signal: caricamento, errore, dati.
 * Le pagine lo usano per mostrare in modo uniforme spinner, stato vuoto ed errore.
 */
export class LoadState<T> {
  readonly data = signal<T | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  async run(task: () => Promise<T>): Promise<T | null> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await task();
      this.data.set(result);
      return result;
    } catch (e) {
      this.error.set(errorMessage(e));
      return null;
    } finally {
      this.loading.set(false);
    }
  }
}
