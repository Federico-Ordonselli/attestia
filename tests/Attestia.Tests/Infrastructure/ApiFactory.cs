using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Attestia.Tests.Infrastructure;

/// <summary>
/// Host dell'API in memoria collegato a un database di test reale. L'orologio e' fisso
/// (15/06/2026, 12:00 a Roma) cosi' gli stati degli attestati sono deterministici.
/// </summary>
public sealed class ApiFactory(string connectionString, Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Attestia", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            configure?.Invoke(services);
        });
    }

    /// <summary>Client HTTPS che si presenta come <paramref name="employeeNumber"/> con il ruolo indicato (null = anonimo).</summary>
    public HttpClient ClientAs(string? employeeNumber, string? role = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (employeeNumber is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, $"{employeeNumber};{role}");
        }

        return client;
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
