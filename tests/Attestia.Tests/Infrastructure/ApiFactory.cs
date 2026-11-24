using System.Net;
using Attestia.Core.Sso;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Attestia.Tests.Infrastructure;

/// <summary>
/// Host dell'API in memoria collegato a un database di test reale. L'orologio e' fisso
/// (15/06/2026, 12:00 a Roma) cosi' gli stati degli attestati sono deterministici.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    Action<IServiceCollection>? configure = null,
    DateTimeOffset? now = null,
    int uploadsPerMinute = 1000,
    bool useTestAuth = true)
    : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Chiave SSO dei test (41 byte, solo per i test).</summary>
    public const string SsoKey = Attestia.Tests.Sso.SsoCookieTests.VectorKeyBase64;

    public FakeTimeProvider Time { get; } = new(now ?? Now);

    public SsoCookie Sso { get; } = SsoCookie.FromBase64Key(SsoKey);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Attestia", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("RateLimits:UploadsPerMinute", uploadsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting(SsoCookie.KeyEnvironmentVariable, SsoKey);
        builder.UseSetting("Saml:BaseUrl", "https://localhost");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            configure?.Invoke(services);
            if (!useTestAuth)
            {
                return;
            }

            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        });
    }

    /// <summary>
    /// Client HTTPS che si presenta come <paramref name="employeeNumber"/> con il ruolo indicato (null = anonimo).
    /// Come Angular: chiama /api/me per ottenere il token CSRF e lo rimanda nell'header X-XSRF-TOKEN.
    /// </summary>
    public HttpClient ClientAs(string? employeeNumber, string? role = null)
    {
        var client = NewClient(new CookieContainer());
        if (employeeNumber is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, $"{employeeNumber};{role}");
            client.GetAsync("/api/me").GetAwaiter().GetResult().Dispose();
        }

        return client;
    }

    /// <summary>Client con il vero cookie <c>attestia_sso</c> (per i test dell'handler di produzione).</summary>
    public HttpClient ClientWithSsoCookie(string? cookieValue, CookieContainer? cookies = null)
    {
        cookies ??= new CookieContainer();
        if (cookieValue is not null)
        {
            cookies.Add(BaseUri, new Cookie(SsoCookie.CookieName, cookieValue, "/") { Secure = true, HttpOnly = true });
        }

        return NewClient(cookies);
    }

    public static readonly Uri BaseUri = new("https://localhost");

    public HttpClient NewClient(CookieContainer cookies) =>
        CreateDefaultClient(BaseUri, new XsrfHeaderHandler(cookies), new CookieContainerHandler(cookies));

    /// <summary>Copia il cookie XSRF-TOKEN nell'header X-XSRF-TOKEN delle richieste di scrittura (come fa Angular).</summary>
    private sealed class XsrfHeaderHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var safe = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
            var token = cookies.GetCookies(BaseUri)["XSRF-TOKEN"]?.Value;
            if (!safe && token is not null && !request.Headers.Contains("X-XSRF-TOKEN"))
            {
                request.Headers.Add("X-XSRF-TOKEN", token);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>Database con lo scenario dei report (vedi ReportScenario) + host API, condivisi da una classe di test.</summary>
public sealed class ApiScenario : IDisposable
{
    public ApiScenario(MsSqlFixture server)
    {
        Database = new Database.ReportScenario(server);
        Factory = new ApiFactory(Database.ConnectionString);
    }

    public Database.ReportScenario Database { get; }

    public ApiFactory Factory { get; }

    public void Dispose() => Factory.Dispose();
}
