using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Attestia.Api.Auth;

internal static class AuthSchemes
{
    /// <summary>Schema di autenticazione basato sul cookie <c>attestia_sso</c> (vedi fase 5).</summary>
    public const string SsoCookie = "AttestiaSso";
}

/// <summary>
/// Segnaposto della fase 3: nessuna richiesta risulta autenticata finche' non arriva il cookie SSO.
/// I test sostituiscono lo schema con un handler di test.
/// </summary>
internal sealed class NoSessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
