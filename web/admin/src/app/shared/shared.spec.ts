import { TestBed } from '@angular/core/testing';
import { ItDatePipe } from './it-date.pipe';
import { StatusBadge } from './status-badge';

describe('ItDatePipe', () => {
  const pipe = new ItDatePipe();
  it('formatta una data ISO senza passare da Date (niente slittamenti di fuso)', () => {
    expect(pipe.transform('2026-03-01')).toBe('01/03/2026');
    expect(pipe.transform(null)).toBe('—');
    expect(pipe.transform(undefined, 'Non scade')).toBe('Non scade');
  });
});

describe('StatusBadge', () => {
  it.each([
    ['Valid', 'Valido', 'text-bg-success'],
    ['ExpiringSoon', 'In scadenza', 'text-bg-warning'],
    ['Expired', 'Scaduto', 'text-bg-danger'],
    ['Missing', 'Mancante', 'text-bg-secondary'],
  ] as const)('%s mostra il testo "%s" (non solo il colore)', async (status, text, css) => {
    const fixture = TestBed.createComponent(StatusBadge);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    const badge = (fixture.nativeElement as HTMLElement).querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe(text);
    expect(badge.classList).toContain(css);
  });
});
