import { TestBed } from '@angular/core/testing';
import { CoursesService } from '../../api/generated/services';
import { ApiError } from '../../core/api-error';
import { settle } from '../../../testing/fakes';
import { CoursesPage } from './courses.page';

describe('CoursesPage', () => {
  const gdpr = { id: 3, code: 'GDPR', title: 'Privacy e protezione dei dati', category: 'Privacy', validityMonths: 24, isMandatory: true, isActive: true };
  let api: Record<string, ReturnType<typeof vi.fn>>;

  async function render() {
    api = {
      listCourses: vi.fn().mockResolvedValue([gdpr]),
      createCourse: vi.fn().mockResolvedValue({ ...gdpr, id: 9, code: 'NUOVO' }),
      updateCourse: vi.fn().mockResolvedValue(gdpr),
      deactivateCourse: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({ imports: [CoursesPage], providers: [{ provide: CoursesService, useValue: api }] });
    const fixture = TestBed.createComponent(CoursesPage);
    await settle(fixture);
    return fixture;
  }

  const type = (fixture: Awaited<ReturnType<typeof render>>, id: string, value: string) => {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const submit = (fixture: Awaited<ReturnType<typeof render>>) =>
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));

  it('non invia un codice non valido e mostra l\'errore accanto al campo', async () => {
    const fixture = await render();
    type(fixture, 'code', 'minuscolo');
    type(fixture, 'title', 'Titolo valido');
    type(fixture, 'category', 'Test');
    submit(fixture);
    await settle(fixture);

    expect(api['createCourse']).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('#code')!.classList).toContain('is-invalid');
  });

  it('crea un corso con validità vuota = non scade', async () => {
    const fixture = await render();
    type(fixture, 'code', 'NUOVO');
    type(fixture, 'title', 'Corso nuovo');
    type(fixture, 'category', 'Test');
    submit(fixture);
    await settle(fixture);

    expect(api['createCourse']).toHaveBeenCalledWith({ body: { code: 'NUOVO', title: 'Corso nuovo', category: 'Test', validityMonths: null, isMandatory: false } });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Corso NUOVO creato.');
  });

  it('mostra il messaggio di conflitto dell\'API (codice duplicato)', async () => {
    const fixture = await render();
    api['createCourse'].mockRejectedValueOnce(new ApiError(409, 'Conflitto', 'Esiste già un corso con questo codice.'));
    type(fixture, 'code', 'GDPR');
    type(fixture, 'title', 'Duplicato');
    type(fixture, 'category', 'Test');
    submit(fixture);
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')!.textContent).toContain('Esiste già un corso');
  });

  it('la disattivazione richiede una conferma esplicita (niente window.confirm)', async () => {
    const fixture = await render();
    const html = fixture.nativeElement as HTMLElement;
    const find = (text: string) => Array.from(html.querySelectorAll('button')).find((b) => b.textContent?.includes(text));

    find('Disattiva')!.click();
    fixture.detectChanges();
    expect(api['deactivateCourse']).not.toHaveBeenCalled();
    find('Conferma disattivazione')!.click();
    await settle(fixture);

    expect(api['deactivateCourse']).toHaveBeenCalledWith({ id: 3 });
  });
});
