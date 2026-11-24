using System.Security.Claims;
using System.Text.Encodings.Web;
using Attestia.Core.Security;
using Attestia.Core.Sso;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Attestia.Api.Auth;

internal static class AuthSchemes
{
    /// <summary>Schema di autenticazione basato sul cookie firmato <c>attestia_sso</c>.</summary>
    public const string SsoCookie = "AttestiaSso";
}

/// <summary>
/// Autentica le richieste API con il cookie <c>attestia_sso</c> emesso da /sso/acs: e' la stessa sessione
/// letta dal portale ASP classic. Nessun redirect: l'API risponde 401/403 in ProblemDetails e il client
/// (Angular) decide di mandare l'utente a /sso/login.
/// </summary>
internal sealed partial class SsoCookieAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SsoCookie cookie,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SsoCookie.CookieName, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var result = cookie.Validate(value, TimeProvider.GetUtcNow());
        if (!result.IsValid)
        {
            // Si registra il motivo, mai il valore del cookie.
            Rejected(Logger, result.Error);
            return Task.FromResult(AuthenticateResult.Fail($"Cookie SSO non valido: {result.Error}"));
        }

        var session = result.Session!;
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, session.EmployeeNumber),
                new Claim(AttestiaClaims.EmployeeNumber, session.EmployeeNumber),
                new Claim(AttestiaClaims.Role, session.Role),
            ],
            AuthSchemes.SsoCookie, ClaimTypes.Name, AttestiaClaims.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = session.ExpiresAt }, AuthSchemes.SsoCookie);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteProblem(StatusCodes.Status401Unauthorized, "Autenticazione richiesta", "Sessione assente o scaduta: accedere tramite /sso/login.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblem(StatusCodes.Status403Forbidden, "Accesso negato", "Il ruolo dell'utente non consente questa operazione.");

    private async Task WriteProblem(int status, string title, string detail)
    {
        Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cookie SSO rifiutato: {Reason}")]
    private static partial void Rejected(ILogger logger, SsoCookieError reason);
}

/// <summary>Scrive e cancella i cookie SSO con <c>Set-Cookie</c> esplicito (vedi DECISIONS D23).</summary>
internal static class SsoCookieWriter
{
    /// <summary>
    /// <c>Response.Cookies.Append</c> applicherebbe la codifica URL al valore (<c>|</c> diventa <c>%7C</c>):
    /// il portale ASP classic deve invece leggere esattamente i byte firmati.
    /// </summary>
    public static void Write(HttpResponse response, string value, DateTimeOffset expiresAt, TimeSpan lifetime)
    {
        var header = new SetCookieHeaderValue(SsoCookie.CookieName, value)
        {
            Path = "/",
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.Net.Http.Headers.SameSiteMode.Lax,
            Expires = expiresAt,
            MaxAge = lifetime,
        };
        response.Headers.Append(HeaderNames.SetCookie, header.ToString());
    }

    public static void Delete(HttpResponse response)
    {
        var header = new SetCookieHeaderValue(SsoCookie.CookieName, "")
        {
            Path = "/",
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.Net.Http.Headers.SameSiteMode.Lax,
            Expires = DateTimeOffset.UnixEpoch,
            MaxAge = TimeSpan.Zero,
        };
        response.Headers.Append(HeaderNames.SetCookie, header.ToString());
    }
}
