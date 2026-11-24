using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Attestia.Api.Auth;

/// <summary>
/// Protezione CSRF per TUTTE le richieste di scrittura su /api (JSON e multipart), con il pattern
/// "double submit" di Angular: l'API emette un cookie leggibile <c>XSRF-TOKEN</c>, Angular lo rimanda
/// nell'header <c>X-XSRF-TOKEN</c>. Il token e' legato all'utente autenticato (ASP.NET Antiforgery).
/// Si aggiunge a SameSite=Lax del cookie di sessione (difesa in profondita').
/// </summary>
internal static class CsrfProtection
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string RequestTokenCookie = "XSRF-TOKEN";

    public static IServiceCollection AddCsrfProtection(this IServiceCollection services) =>
        services.AddAntiforgery(o =>
        {
            o.HeaderName = HeaderName;
            o.Cookie.Name = "attestia_af";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
        });

    /// <summary>Emette (o rinnova) la coppia cookie antiforgery + cookie XSRF-TOKEN leggibile da JavaScript.</summary>
    public static void IssueToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(RequestTokenCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // deve leggerlo Angular
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    /// <summary>Da usare dopo UseAuthorization: le richieste anonime ricevono prima 401, non un errore CSRF.</summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
            if (unsafeMethod && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(context))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "Token CSRF mancante o non valido",
                            Detail = "Ricaricare la pagina e riprovare.",
                        },
                    });
                    return;
                }
            }

            await next(context);
        });
}
