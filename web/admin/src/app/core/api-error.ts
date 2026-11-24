import { HttpErrorResponse } from '@angular/common/http';

/** Errore applicativo normalizzato a partire da un ProblemDetails (RFC 9457) dell'API. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
    readonly extensions: Readonly<Record<string, unknown>> = {},
  ) {
    super(detail ?? title);
    this.name = 'ApiError';
  }

  /** Messaggio da mostrare all'utente (mai dettagli tecnici). */
  get userMessage(): string {
    if (this.status === 0) {
      return 'Impossibile contattare il server. Verifica la connessione e riprova.';
    }
    return this.detail ?? this.title;
  }

  static fromHttp(error: HttpErrorResponse): ApiError {
    const body: unknown = error.error;
    if (body && typeof body === 'object' && ('title' in body || 'detail' in body)) {
      const { title, detail, errors, status, type, traceId, ...rest } = body as Record<string, unknown>;
      void status;
      void type;
      void traceId;
      return new ApiError(
        error.status,
        typeof title === 'string' ? title : 'Errore',
        typeof detail === 'string' ? detail : null,
        (errors as Record<string, string[]>) ?? {},
        rest,
      );
    }
    const fallback = error.status === 0 ? 'Server non raggiungibile' : `Errore ${error.status}`;
    return new ApiError(error.status, fallback, null);
  }
}

export function errorMessage(error: unknown): string {
  return error instanceof ApiError ? error.userMessage : 'Si è verificato un errore inatteso.';
}
