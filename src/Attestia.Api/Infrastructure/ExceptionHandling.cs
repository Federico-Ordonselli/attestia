using Attestia.Core.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// Traduce le eccezioni in ProblemDetails. Solo le <see cref="DomainException"/> espongono il proprio messaggio
/// (scritto apposta per l'utente); ogni altra eccezione produce un 500 generico, con il dettaglio solo nei log.
/// </summary>
internal sealed partial class ExceptionToProblemDetailsHandler(IProblemDetailsService problemDetails, ILogger<ExceptionToProblemDetailsHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Il client ha chiuso la connessione: niente da rispondere, niente da segnalare come errore.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, title, detail) = exception switch
        {
            ConflictException e => (StatusCodes.Status409Conflict, "Conflitto", e.Message),
            UnprocessableException e => (StatusCodes.Status422UnprocessableEntity, "Richiesta non applicabile", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Richiesta non valida", "La richiesta non è valida."),
            _ => (StatusCodes.Status500InternalServerError, "Errore interno", "Si è verificato un errore inatteso. Riprova più tardi."),
        };

        if (status >= 500)
        {
            UnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Eccezione non gestita su {Method} {Path}")]
    private static partial void UnhandledException(ILogger logger, Exception exception, string method, string path);
}
