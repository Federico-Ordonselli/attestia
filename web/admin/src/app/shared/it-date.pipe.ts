import { Pipe, PipeTransform } from '@angular/core';

/**
 * "2026-03-10" → "10/03/2026". Le date dell'API sono date di calendario (senza ora né fuso):
 * non passano da Date, che le interpreterebbe come mezzanotte UTC.
 */
@Pipe({ name: 'itDate' })
export class ItDatePipe implements PipeTransform {
  transform(value: string | null | undefined, empty = '—'): string {
    if (!value) {
      return empty;
    }
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
    return match ? `${match[3]}/${match[2]}/${match[1]}` : value;
  }
}
