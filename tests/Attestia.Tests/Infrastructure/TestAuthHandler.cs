using System.Security.Claims;
using System.Text.Encodings.Web;
using Attestia.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Attestia.Tests.Infrastructure;

/// <summary>
/// Autenticazione SOLO di test: l'header <c>X-Test-User: MATRICOLA;Ruolo</c> produce gli stessi claim
/// che in produzione emette il cookie SSO. Registrato soltanto dalla <see cref="ApiFactory"/>.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || value.ToString().Split(';') is not [var number, var role])
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, number), new Claim(AttestiaClaims.EmployeeNumber, number), new Claim(AttestiaClaims.Role, role)],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
