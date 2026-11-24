using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Attestia.Api.Saml;
using Attestia.Tests.Infrastructure;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Attestia.Tests.Sso;

/// <summary>IdP SAML finto in memoria: un certificato autofirmato con cui firmare le risposte nei test.</summary>
public sealed class TestIdp : ISamlConfigurationProvider, IDisposable
{
    public const string EntityId = "urn:test:idp";

    public TestIdp()
    {
        Certificate = CreateCertificate("CN=Attestia Test IdP");
        Impostor = CreateCertificate("CN=Impostor");
    }

    public X509Certificate2 Certificate { get; }

    /// <summary>Certificato NON pubblicato nei metadata: una risposta firmata con questo deve essere rifiutata.</summary>
    public X509Certificate2 Impostor { get; }

    public Saml2Configuration ServiceProvider => MetadataSamlConfigurationProvider.CreateBase(Options);

    public static SamlOptions Options { get; } = new() { BaseUrl = "https://localhost", EntityId = "urn:attestia:sp" };

    public Task<Saml2Configuration> GetAsync(CancellationToken ct) =>
        Task.FromResult(MetadataSamlConfigurationProvider.Complete(ServiceProvider, EntityId,
            new Uri("https://idp.example.test/sso"), new Uri("https://idp.example.test/slo"), [X509CertificateLoader.LoadCertificate(Certificate.RawData)]));

    /// <summary>Costruisce il valore del campo SAMLResponse (POST binding) firmato.</summary>
    public string Response(string inResponseTo, IEnumerable<Claim> attributes, X509Certificate2? signer = null, string audience = "urn:attestia:sp",
        DateTime? notBefore = null, DateTime? expires = null)
    {
        var config = new Saml2Configuration
        {
            Issuer = EntityId,
            SigningCertificate = signer ?? Certificate,
            SignatureAlgorithm = SecurityAlgorithms.RsaSha256Signature,
            AuthnResponseSignType = Saml2AuthnResponseSignTypes.SignAssertionAndResponse,
        };
        var response = new Saml2AuthnResponse(config)
        {
            InResponseTo = new Saml2Id(inResponseTo),
            Status = Saml2StatusCodes.Success,
            Destination = Options.AcsUrl,
            NameId = new Saml2NameIdentifier("utente-test"),
            ClaimsIdentity = new ClaimsIdentity(attributes),
        };
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = response.ClaimsIdentity,
            Audience = audience,
            Issuer = EntityId,
            IssuedAt = notBefore ?? now,
            NotBefore = notBefore ?? now,
            Expires = expires ?? now.AddMinutes(5),
        };
        var subjectConfirmation = new Saml2SubjectConfirmation(Microsoft.IdentityModel.Tokens.Saml2.Saml2Constants.ConfirmationMethods.Bearer,
            new Saml2SubjectConfirmationData { InResponseTo = new Saml2Id(inResponseTo), NotOnOrAfter = (expires ?? now.AddMinutes(5)), Recipient = Options.AcsUrl });
        var statement = new Saml2AuthenticationStatement(new Saml2AuthenticationContext(AuthnContextClassTypes.PasswordProtectedTransport))
        {
            SessionIndex = "sessione-1",
            AuthenticationInstant = now,
        };
        response.CreateSecurityToken(descriptor, statement, subjectConfirmation);

        var binding = new Saml2PostBinding();
        binding.Bind(response);
        return WebUtility.HtmlDecode(Regex.Match(binding.PostContent, "name=\"SAMLResponse\" value=\"([^\"]+)\"").Groups[1].Value);
    }

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        Impostor.Dispose();
    }
}

public sealed class SsoScenario : IDisposable
{
    public SsoScenario(MsSqlFixture server)
    {
        Database = new Database.ReportScenario(server); // dipendenti A1..B3 (B3 non attivo)
        // Ora reale: CookieContainer scarta i cookie con Expires nel passato rispetto al vero orologio.
        Factory = new ApiFactory(Database.ConnectionString, s => s.AddSingleton<ISamlConfigurationProvider>(Idp), now: DateTimeOffset.UtcNow, useTestAuth: false);
    }

    public TestIdp Idp { get; } = new();

    public Database.ReportScenario Database { get; }

    public ApiFactory Factory { get; }

    public void Dispose()
    {
        Factory.Dispose();
        Idp.Dispose();
    }
}

/// <summary>Sessione SSO reale: handler del cookie attestia_sso, ACS SAML, CSRF. Nessun handler di test.</summary>
[Trait("Category", "Integration")]
public sealed class SsoFlowTests(SsoScenario scenario) : IClassFixture<SsoScenario>
{
    private readonly ApiFactory _api = scenario.Factory;
    private readonly TestIdp _idp = scenario.Idp;

    private static Claim[] Attributes(string number, string role) => [new("employeeNumber", number), new("role", role), new("displayName", "Nome Test")];

    /// <summary>GET /sso/login: restituisce l'ID dell'AuthnRequest (dal parametro SAMLRequest deflate+base64).</summary>
    private static async Task<(string RequestId, Uri Location)> StartLogin(HttpClient client, string? returnUrl = null)
    {
        using var response = await client.GetAsync("/sso/login" + (returnUrl is null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var samlRequest = System.Web.HttpUtility.ParseQueryString(location.Query)["SAMLRequest"]!;
        await using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(samlRequest)), CompressionMode.Decompress);
        var xml = XDocument.Load(inflate);
        return (xml.Root!.Attribute("ID")!.Value, location);
    }

    private static Task<HttpResponseMessage> PostToAcs(HttpClient client, string samlResponse) =>
        client.PostAsync("/sso/acs", new FormUrlEncodedContent(new Dictionary<string, string> { ["SAMLResponse"] = samlResponse }));

    [Fact]
    public async Task Full_login_issues_a_signed_session_cookie_usable_by_the_api()
    {
        var cookies = new CookieContainer();
        using var client = _api.NewClient(cookies);

        var (requestId, location) = await StartLogin(client, "/admin/scadenze");
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "HR")));
        var setCookie = acs.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("attestia_sso=", StringComparison.Ordinal));
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");

        Assert.Equal("idp.example.test", location.Host);
        Assert.Equal(HttpStatusCode.Redirect, acs.StatusCode);
        Assert.Equal("/admin/scadenze", acs.Headers.Location!.OriginalString);
        Assert.Matches(@"^attestia_sso=v1\|B1\|HR\|\d+\|[0-9a-f]{64};", setCookie); // valore NON codificato
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=3600", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("B1", me.GetProperty("employeeNumber").GetString());
        Assert.Equal("HR", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Employee_lands_on_the_portal_by_default()
    {
        using var client = _api.NewClient(new CookieContainer());

        var (requestId, _) = await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("A1", "Employee")));

        Assert.Equal("/portale/", acs.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("https://evil.example.test/")]
    [InlineData("//evil.example.test/")]
    [InlineData("/\\evil.example.test/")]
    public async Task Return_url_cannot_point_outside_the_site(string returnUrl)
    {
        using var client = _api.NewClient(new CookieContainer());

        var (requestId, _) = await StartLogin(client, returnUrl);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "HR")));

        Assert.Equal("/admin/", acs.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Response_signed_by_an_unknown_certificate_is_rejected()
    {
        using var client = _api.NewClient(new CookieContainer());

        var (requestId, _) = await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "Admin"), signer: _idp.Impostor));

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
        Assert.False(acs.Headers.Contains("Set-Cookie") && acs.Headers.GetValues("Set-Cookie").Any(h => h.StartsWith("attestia_sso=v1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Response_for_another_audience_is_rejected()
    {
        using var client = _api.NewClient(new CookieContainer());

        var (requestId, _) = await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "HR"), audience: "urn:other:sp"));

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
    }

    [Fact]
    public async Task Expired_assertion_is_rejected()
    {
        using var client = _api.NewClient(new CookieContainer());
        var past = DateTime.UtcNow.AddHours(-2);

        var (requestId, _) = await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "HR"), notBefore: past, expires: past.AddMinutes(5)));

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
    }

    [Fact]
    public async Task Response_to_a_different_request_is_rejected_in_response_to()
    {
        using var client = _api.NewClient(new CookieContainer());

        await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response("_id-di-un-altra-richiesta", Attributes("B1", "HR")));

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
    }

    [Fact]
    public async Task Unsolicited_response_without_a_pending_login_is_rejected()
    {
        using var client = _api.NewClient(new CookieContainer());

        using var acs = await PostToAcs(client, _idp.Response("_qualsiasi", Attributes("B1", "HR")));

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
    }

    [Theory]
    [InlineData("B1", "Root")]     // ruolo non previsto
    [InlineData("bad id", "HR")]   // matricola non valida
    [InlineData("B3", "HR")]       // dipendente non attivo
    [InlineData("ZZ99", "HR")]     // dipendente sconosciuto
    public async Task Valid_assertion_for_an_unauthorized_identity_is_forbidden(string number, string role)
    {
        using var client = _api.NewClient(new CookieContainer());

        var (requestId, _) = await StartLogin(client);
        using var acs = await PostToAcs(client, _idp.Response(requestId, Attributes(number, role)));

        Assert.Equal(HttpStatusCode.Forbidden, acs.StatusCode);
    }

    [Fact]
    public async Task Garbage_saml_response_is_rejected_without_details()
    {
        using var client = _api.NewClient(new CookieContainer());

        await StartLogin(client);
        using var acs = await PostToAcs(client, Convert.ToBase64String(Encoding.UTF8.GetBytes("<not-saml/>")));
        var body = await acs.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, acs.StatusCode);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_session_cookie_gets_401()
    {
        var valid = _api.Sso.Issue("B1", "HR", _api.Time.GetUtcNow().AddMinutes(30));
        var tampered = valid.Replace("|HR|", "|Admin|", StringComparison.Ordinal);
        using var client = _api.ClientWithSsoCookie(tampered);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_session_cookie_gets_401()
    {
        using var client = _api.ClientWithSsoCookie(_api.Sso.Issue("B1", "HR", _api.Time.GetUtcNow().AddSeconds(-1)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_signed_with_another_key_gets_401()
    {
        var foreign = new Attestia.Core.Sso.SsoCookie(RandomNumberGenerator.GetBytes(32)).Issue("B1", "Admin", _api.Time.GetUtcNow().AddHours(1));
        using var client = _api.ClientWithSsoCookie(foreign);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Employee_session_is_forbidden_on_hr_endpoints_and_sees_only_own_data()
    {
        using var client = _api.ClientWithSsoCookie(_api.Sso.Issue("A1", "Employee", _api.Time.GetUtcNow().AddMinutes(30)));

        using var other = await client.GetAsync("/api/employees/A2/certificates");
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/me/certificates");

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal("application/problem+json", other.Content.Headers.ContentType?.MediaType);
        Assert.Equal("A1", mine.GetProperty("employee").GetProperty("employeeNumber").GetString());
    }

    [Fact]
    public async Task Writes_require_the_csrf_token_bound_to_the_session()
    {
        var cookies = new CookieContainer();
        using var client = _api.ClientWithSsoCookie(_api.Sso.Issue("B2", "Admin", _api.Time.GetUtcNow().AddMinutes(30)), cookies);
        var body = new { code = "CSRF-1", title = "Corso CSRF", category = "Test", isMandatory = false };

        using var withoutToken = await client.PostAsJsonAsync("/api/courses", body);   // nessun token ancora emesso
        (await client.GetAsync("/api/me")).Dispose();                                   // emette XSRF-TOKEN
        using var withToken = await client.PostAsJsonAsync("/api/courses", body);
        using var forged = new HttpRequestMessage(HttpMethod.Delete, "/api/courses/1");
        forged.Headers.Add("X-XSRF-TOKEN", "token-inventato");
        using var forgedResponse = await client.SendAsync(forged);

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, forgedResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session_and_asks_the_idp_to_log_out()
    {
        var cookies = new CookieContainer();
        using var client = _api.NewClient(cookies);
        var (requestId, _) = await StartLogin(client);
        (await PostToAcs(client, _idp.Response(requestId, Attributes("B1", "HR")))).Dispose();

        using var logout = await client.GetAsync("/sso/logout");
        using var after = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("idp.example.test", logout.Headers.Location!.Host);
        Assert.Contains("SAMLRequest=", logout.Headers.Location.Query, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Logout_response_posted_by_the_idp_ends_on_the_logged_out_page()
    {
        using var client = _api.ClientWithSsoCookie(_api.Sso.Issue("B1", "HR", _api.Time.GetUtcNow().AddMinutes(30)));

        // Keycloak con "force POST binding" rimanda la LogoutResponse in POST (scoperto provando con Keycloak reale).
        using var response = await client.PostAsync("/sso/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["SAMLResponse"] = "PHJlc3BvbnNlLz4=" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sso/logged-out", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("attestia_sso=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Metadata_describes_the_service_provider()
    {
        using var client = _api.NewClient(new CookieContainer());

        var xml = XDocument.Parse(await client.GetStringAsync("/sso/metadata"));
        XNamespace md = "urn:oasis:names:tc:SAML:2.0:metadata";

        Assert.Equal("urn:attestia:sp", xml.Root!.Attribute("entityID")!.Value);
        var sp = xml.Root.Element(md + "SPSSODescriptor")!;
        Assert.Equal("true", sp.Attribute("WantAssertionsSigned")!.Value);
        Assert.Equal("https://localhost/sso/acs", sp.Element(md + "AssertionConsumerService")!.Attribute("Location")!.Value);
    }
}
