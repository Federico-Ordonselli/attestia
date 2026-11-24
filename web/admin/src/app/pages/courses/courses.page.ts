import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Course } from '../../api/generated/models';
import { CoursesService } from '../../api/generated/services';
import { ApiError, errorMessage } from '../../core/api-error';
import { LoadState } from '../../shared/load-state';
import { Empty, ErrorPanel, Loading } from '../../shared/state-panels';

/** Anagrafica corsi (solo Admin). "Elimina" = disattivazione: gli attestati esistenti restano. */
@Component({
  selector: 'app-courses-page',
  imports: [ReactiveFormsModule, Loading, ErrorPanel, Empty],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './courses.page.html',
})
export class CoursesPage {
  private readonly api = inject(CoursesService);
  protected readonly state = new LoadState<Course[]>();
  protected readonly editing = signal<Course | null>(null);
  protected readonly confirmingDeactivation = signal<number | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Z0-9-]{2,20}$/)]],
    title: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(200)]],
    category: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    validityMonths: [null as number | null, [Validators.min(1), Validators.max(240)]],
    isMandatory: [false],
    isActive: [true],
  });

  constructor() {
    void this.reload();
  }

  protected reload(): Promise<unknown> {
    return this.state.run(() => this.api.listCourses({ includeInactive: true }));
  }

  protected edit(course: Course): void {
    this.editing.set(course);
    this.formError.set(null);
    this.form.reset({ ...course, validityMonths: course.validityMonths ?? null });
    this.form.controls.code.disable(); // il codice non si modifica: è la chiave usata dall'LMS
  }

  protected newCourse(): void {
    this.editing.set(null);
    this.formError.set(null);
    this.form.reset();
    this.form.controls.code.enable();
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    const validityMonths = v.validityMonths === null || String(v.validityMonths) === '' ? null : Number(v.validityMonths);
    this.saving.set(true);
    this.formError.set(null);
    try {
      const current = this.editing();
      if (current) {
        await this.api.updateCourse({ id: current.id, body: { title: v.title, category: v.category, validityMonths, isMandatory: v.isMandatory, isActive: v.isActive } });
        this.message.set(`Corso ${current.code} aggiornato.`);
      } else {
        await this.api.createCourse({ body: { code: v.code, title: v.title, category: v.category, validityMonths, isMandatory: v.isMandatory } });
        this.message.set(`Corso ${v.code} creato.`);
      }
      this.newCourse();
      await this.reload();
    } catch (e) {
      this.formError.set(e instanceof ApiError && Object.keys(e.fieldErrors).length > 0
        ? Object.values(e.fieldErrors).flat().join(' ')
        : errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected async deactivate(course: Course): Promise<void> {
    try {
      await this.api.deactivateCourse({ id: course.id });
      this.message.set(`Corso ${course.code} disattivato.`);
      this.confirmingDeactivation.set(null);
      await this.reload();
    } catch (e) {
      this.message.set(errorMessage(e));
    }
  }

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || c.dirty);
  }
}
