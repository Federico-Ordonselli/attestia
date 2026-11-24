using System.Collections.Specialized;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Attestia.Api.Auth;
using Attestia.Core.Security;
using Attestia.Core.Sso;
using Attestia.Data.Repositories;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Claims;
using ITfoxtec.Identity.Saml2.Cryptography;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.AspNetCore.DataProtection;

namespace Attestia.Api.Saml;

/// <summary>
/// Service Provider SAML 2.0 (ITfoxtec): /sso/login, /sso/acs, /sso/logout, /sso/metadata.
/// Dopo un'asserzione valida emette il cookie <c>attestia_sso</c>, unica sessione per API, back-office e portale classic.
/// </summary>
internal static partial class SsoEndpoints
{
    private const string RequestCookie = "attestia_saml_req";
    private const string SessionCookie = "attestia_saml_sid";
    private const string RequestPurpose = "Attestia.Saml.AuthnRequest.v1";
    private const string SessionPurpose = "Attestia.Saml.Session.v1";

    public static IEndpointRouteBuilder MapSso(this IEndpointRouteBuilder app)
    {
        var sso = app.MapGroup("/sso").AllowAnonymous().ExcludeFromDescription();

        sso.MapGet("/metadata", (ISamlConfigurationProvider saml, SamlOptions options) =>
        {
            var descriptor = new EntityDescriptor(saml.ServiceProvider, signMetadata: false)
            {
                ValidUntil = 365,
                SPSsoDescriptor = new SPSsoDescriptor
                {
                    AuthnRequestsSigned = false,
                    WantAssertionsSigned = true,
                    NameIDFormats = [NameIdentifierFormats.Unspecified],
                    AssertionConsumerServices = [new AssertionConsumerService { Binding = ProtocolBindings.HttpPost, Location = options.AcsUrl }],
                    SingleLogoutServices = [new SingleLogoutService { Binding = ProtocolBindings.HttpRedirect, Location = options.LogoutUrl }],
                },
            };
            return Results.Content(new Saml2Metadata(descriptor).CreateMetadata().ToXml(), "application/samlmetadata+xml");
        });

        sso.MapGet("/login", async (HttpContext http, ISamlConfigurationProvider saml, SamlOptions options, IDataProtectionProvider dp,
            string? returnUrl, CancellationToken ct) =>
        {
            var config = await saml.GetAsync(ct);
            var request = new Saml2AuthnRequest(config)
            {
                AssertionConsumerServiceUrl = options.AcsUrl,
                ProtocolBinding = ProtocolBindings.HttpPost,
            };
            var binding = new Saml2RedirectBinding();
            binding.Bind(request);

            // Si ricorda l'ID della richiesta per verificare InResponseTo: cookie cifrato e a tempo (10 minuti).
            // SameSite=None perche' l'ACS riceve un POST cross-site dall'IdP.
            var protector = dp.CreateProtector(RequestPurpose).ToTimeLimitedDataProtector();
            var state = protector.Protect($"{request.IdAsString}\n{SafeReturnUrl(returnUrl)}", TimeSpan.FromMinutes(10));
            http.Response.Cookies.Append(RequestCookie, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/sso",
                MaxAge = TimeSpan.FromMinutes(10),
            });
            return Results.Redirect(binding.RedirectLocation.OriginalString);
        });

        sso.MapPost("/acs", async (HttpContext http, ISamlConfigurationProvider saml, SamlOptions options, IDataProtectionProvider dp,
            SsoCookie ssoCookie, IEmployeeRepository employees, TimeProvider time, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Attestia.Sso");
            if (!http.Request.Cookies.TryGetValue(RequestCookie, out var state) || !TryUnprotect(dp.CreateProtector(RequestPurpose).ToTimeLimitedDataProtector(), state, out var expectedId, out var returnUrl))
            {
                AcsRejected(logger, "richiesta di login assente o scaduta");
                return LoginFailed();
            }

            http.Response.Cookies.Delete(RequestCookie, new CookieOptions { Path = "/sso", Secure = true, SameSite = SameSiteMode.None });

            var request = await ToSamlRequest(http.Request, ct);
            Saml2AuthnResponse response;
            try
            {
                response = Validate(request, await saml.GetAsync(ct));
            }
            catch (InvalidSignatureException) when (saml.TryInvalidate())
            {
                // Probabile rotazione delle chiavi dell'IdP: si rileggono i metadata e si ritenta UNA volta.
                MetadataReloaded(logger);
                try
                {
                    response = Validate(request, await saml.GetAsync(ct));
                }
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    AcsRejectedWithException(logger, ex.GetType().Name, ex);
                    return LoginFailed();
                }
            }
#pragma warning disable CA1031 // qualunque asserzione non valida deve diventare un rifiuto generico, senza dettagli all'utente
            catch (Exception ex)
#pragma warning restore CA1031
            {
                AcsRejectedWithException(logger, ex.GetType().Name, ex);
                return LoginFailed();
            }

            if (response.Status != Saml2StatusCodes.Success)
            {
                AcsRejected(logger, $"stato {response.Status}");
                return LoginFailed();
            }

            if (!string.Equals(response.InResponseToAsString, expectedId, StringComparison.Ordinal))
            {
                AcsRejected(logger, "InResponseTo non corrisponde alla richiesta inviata");
                return LoginFailed();
            }

            var attributes = response.ClaimsIdentity;
            var number = attributes.FindFirst("employeeNumber")?.Value;
            var role = attributes.FindFirst("role")?.Value;
            if (number is null || !EmployeeNumberPattern().IsMatch(number) || !Roles.IsValid(role))
            {
                AcsRejected(logger, "attributi employeeNumber/role assenti o non validi");
                return Results.Content(Page("Accesso negato", "Il tuo profilo non ha una matricola o un ruolo validi per Attestia."), "text/html; charset=utf-8", statusCode: 403);
            }

            var employee = await employees.GetByNumberAsync(number, ct);
            if (employee is not { IsActive: true })
            {
                AcsRejected(logger, "dipendente inesistente o non attivo");
                return Results.Content(Page("Accesso negato", "La tua matricola non risulta attiva in Attestia."), "text/html; charset=utf-8", statusCode: 403);
            }

            var lifetime = TimeSpan.FromMinutes(options.SessionMinutes);
            var expiresAt = time.GetUtcNow().Add(lifetime);
            SsoCookieWriter.Write(http.Response, ssoCookie.Issue(number, role!, expiresAt), expiresAt, lifetime);

            // Dati per il logout SAML (NameID + SessionIndex), cifrati e accessibili solo sotto /sso.
            var nameId = attributes.FindFirst(Saml2ClaimTypes.NameId)?.Value ?? "";
            var nameIdFormat = attributes.FindFirst(Saml2ClaimTypes.NameIdFormat)?.Value ?? "";
            var sessionIndex = attributes.FindFirst(Saml2ClaimTypes.SessionIndex)?.Value ?? "";
            http.Response.Cookies.Append(SessionCookie, dp.CreateProtector(SessionPurpose).Protect($"{nameId}\n{nameIdFormat}\n{sessionIndex}"), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/sso",
                MaxAge = lifetime,
            });

            LoggedIn(logger, number, role!);
            var target = returnUrl.Length > 0 ? returnUrl : (role == Roles.Employee ? "/portale/" : "/admin/");
            return Results.Redirect(target);
        }).DisableAntiforgery();

        // GET: avvio del logout dall'SP; GET o POST: risposta/richiesta di logout dall'IdP
        // (Keycloak con "force POST binding" usa il POST binding anche per il logout).
        sso.MapMethods("/logout", [HttpMethods.Get, HttpMethods.Post], async (HttpContext http, ISamlConfigurationProvider saml, IDataProtectionProvider dp, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Attestia.Sso");
            SsoCookieWriter.Delete(http.Response);
            http.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/sso", Secure = true });
            var request = await ToSamlRequest(http.Request, ct);

            var isPost = HttpMethods.IsPost(http.Request.Method);
            if ((request.Query["SAMLResponse"] ?? request.Form["SAMLResponse"]) is not null)
            {
                return Results.Redirect("/sso/logged-out"); // l'IdP conferma il logout
            }

            if ((request.Query["SAMLRequest"] ?? request.Form["SAMLRequest"]) is not null)
            {
                // Logout avviato dall'IdP (es. da un'altra applicazione): si valida la richiesta e si risponde.
                var config = await saml.GetAsync(ct);
                Saml2Binding binding = isPost ? new Saml2PostBinding() : new Saml2RedirectBinding();
                var logoutRequest = new Saml2LogoutRequest(config);
                try
                {
                    binding.Unbind(request, logoutRequest);
                }
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    AcsRejectedWithException(logger, ex.GetType().Name, ex);
                    return Results.Redirect("/sso/logged-out");
                }

                var reply = new Saml2RedirectBinding { RelayState = binding.RelayState };
                reply.Bind(new Saml2LogoutResponse(config) { InResponseToAsString = logoutRequest.IdAsString, Status = Saml2StatusCodes.Success, Destination = config.SingleLogoutDestination });
                return Results.Redirect(reply.RedirectLocation.OriginalString);
            }

            // Logout avviato dall'utente: se conosciamo la sessione SAML chiudiamo anche quella dell'IdP.
            if (!isPost && http.Request.Cookies.TryGetValue(SessionCookie, out var protectedSession) && TryReadSession(dp, protectedSession, out var session))
            {
                try
                {
                    var config = await saml.GetAsync(ct);
                    if (config.SingleLogoutDestination is not null)
                    {
                        var principal = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(Saml2ClaimTypes.NameId, session.NameId),
                            new Claim(Saml2ClaimTypes.NameIdFormat, session.NameIdFormat),
                            new Claim(Saml2ClaimTypes.SessionIndex, session.SessionIndex),
                        ], "saml"));
                        var binding = new Saml2RedirectBinding();
                        binding.Bind(new Saml2LogoutRequest(config, principal) { Destination = config.SingleLogoutDestination });
                        return Results.Redirect(binding.RedirectLocation.OriginalString);
                    }
                }
#pragma warning disable CA1031 // se l'IdP non e' raggiungibile la sessione locale e' comunque chiusa
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    AcsRejectedWithException(logger, ex.GetType().Name, ex);
                }
            }

            return Results.Redirect("/sso/logged-out");
        });

        sso.MapGet("/logged-out", () => Results.Content(
            Page("Disconnesso", "Sei uscito da Attestia. <a href=\"/sso/login\">Accedi di nuovo</a>."), "text/html; charset=utf-8"));

        return app;
    }

    /// <summary>
    /// Legge e valida la risposta: firma (risposta o asserzione), audience, issuer, NotBefore/NotOnOrAfter e replay.
    /// Lo stato non Success viene controllato dal chiamante (Unbind non viene eseguito in quel caso).
    /// </summary>
    private static Saml2AuthnResponse Validate(ITfoxtec.Identity.Saml2.Http.HttpRequest request, Saml2Configuration config)
    {
        var response = new Saml2AuthnResponse(config);
        var binding = new Saml2PostBinding();
        binding.ReadSamlResponse(request, response);
        if (response.Status == Saml2StatusCodes.Success)
        {
            binding.Unbind(request, response);
        }

        return response;
    }

    /// <summary>Solo percorsi locali ("/admin/..."), mai URL assoluti o "//host" (open redirect).</summary>
    internal static string SafeReturnUrl(string? url) =>
        url is { Length: > 0 and < 512 } && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            && !url.Any(char.IsControl)
            ? url
            : "";

    private static bool TryUnprotect(ITimeLimitedDataProtector protector, string value, out string requestId, out string returnUrl)
    {
        requestId = returnUrl = "";
        try
        {
            var parts = protector.Unprotect(value, out _).Split('\n', 2);
            if (parts is not [var id, var url] || id.Length == 0)
            {
                return false;
            }

            (requestId, returnUrl) = (id, SafeReturnUrl(url));
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false; // manomesso, di un'altra installazione o scaduto
        }
    }

    private sealed record SamlSession(string NameId, string NameIdFormat, string SessionIndex);

    private static bool TryReadSession(IDataProtectionProvider dp, string value, out SamlSession session)
    {
        session = new SamlSession("", "", "");
        try
        {
            if (dp.CreateProtector(SessionPurpose).Unprotect(value).Split('\n') is [var n, var f, var s] && n.Length > 0)
            {
                session = new SamlSession(n, f, s);
                return true;
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
        }

        return false;
    }

    /// <summary>Converte la richiesta ASP.NET Core nel modello di ITfoxtec leggendo il form in modo asincrono.</summary>
    private static async Task<ITfoxtec.Identity.Saml2.Http.HttpRequest> ToSamlRequest(HttpRequest request, CancellationToken ct)
    {
        var query = new NameValueCollection();
        foreach (var (key, value) in request.Query)
        {
            query[key] = value.ToString();
        }

        var form = new NameValueCollection();
        if (request.HasFormContentType)
        {
            foreach (var (key, value) in await request.ReadFormAsync(ct))
            {
                form[key] = value.ToString();
            }
        }

        return new ITfoxtec.Identity.Saml2.Http.HttpRequest
        {
            Method = request.Method,
            QueryString = request.QueryString.Value ?? "",
            Query = query,
            Form = form,
        };
    }

    private static IResult LoginFailed() =>
        Results.Content(Page("Accesso non riuscito", "Non è stato possibile completare l'accesso. <a href=\"/sso/login\">Riprova</a>."), "text/html; charset=utf-8", statusCode: 401);

    /// <summary>Pagina minima senza script ne' stili inline (CSP default-src 'none'). I testi sono costanti, non input utente.</summary>
    private static string Page(string title, string bodyHtml) =>
        $"<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>{title} - Attestia</title></head><body><main><h1>{title}</h1><p>{bodyHtml}</p></main></body></html>";

    [GeneratedRegex("^[A-Z0-9-]{2,20}$")]
    private static partial Regex EmployeeNumberPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login SAML rifiutato: {Reason}")]
    private static partial void AcsRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Messaggio SAML non valido ({ExceptionType})")]
    private static partial void AcsRejectedWithException(ILogger logger, string exceptionType, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firma SAML non valida con i metadata in cache: metadata dell'IdP ricaricati")]
    private static partial void MetadataReloaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login SSO riuscito per {EmployeeNumber} con ruolo {Role}")]
    private static partial void LoggedIn(ILogger logger, string employeeNumber, string role);
}

