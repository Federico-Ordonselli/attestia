using System.Net;
using System.Security.Cryptography.X509Certificates;
using Attestia.Api.Saml;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.Extensions.Time.Testing;

namespace Attestia.Tests.Sso;

/// <summary>Rotazione delle chiavi dell'IdP: scoperta ricreando il container Keycloak (nuove chiavi, metadata in cache).</summary>
public sealed class MetadataProviderTests : IDisposable
{
    private readonly TestIdp _a = new();
    private readonly TestIdp _b = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
    private X509Certificate2 _published;
    private int _downloads;

    public MetadataProviderTests() => _published = _a.Certificate;

    private MetadataSamlConfigurationProvider Provider() =>
        new(new SamlOptions { IdpMetadataUrl = "https://idp.example.test/metadata" }, new StubFactory(this), _time);

    private string Metadata()
    {
        _downloads++;
        var descriptor = new EntityDescriptor(new Saml2Configuration { Issuer = TestIdp.EntityId }, signMetadata: false)
        {
            IdPSsoDescriptor = new IdPSsoDescriptor
            {
                SigningCertificates = [X509CertificateLoader.LoadCertificate(_published.RawData)],
                SingleSignOnServices = [new SingleSignOnService { Binding = ProtocolBindings.HttpRedirect, Location = new Uri("https://idp.example.test/sso") }],
                SingleLogoutServices = [new SingleLogoutService { Binding = ProtocolBindings.HttpRedirect, Location = new Uri("https://idp.example.test/slo") }],
            },
        };
        return new Saml2Metadata(descriptor).CreateMetadata().ToXml();
    }

    [Fact]
    public async Task Metadata_are_cached_and_reloaded_after_invalidation()
    {
        using var provider = Provider();

        var first = await provider.GetAsync(CancellationToken.None);
        await provider.GetAsync(CancellationToken.None);
        _published = _b.Certificate;               // l'IdP ruota le chiavi
        var tooEarly = provider.TryInvalidate();    // subito dopo il caricamento: negato
        _time.Advance(TimeSpan.FromMinutes(2));
        var invalidated = provider.TryInvalidate();
        var second = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(_a.Certificate.Thumbprint, Assert.Single(first.SignatureValidationCertificates).Thumbprint);
        Assert.False(tooEarly);
        Assert.True(invalidated);
        Assert.Equal(_b.Certificate.Thumbprint, Assert.Single(second.SignatureValidationCertificates).Thumbprint);
        Assert.Equal(2, _downloads);
        Assert.Equal(TestIdp.EntityId, second.AllowedIssuer);
        Assert.Equal("https://idp.example.test/slo", second.SingleLogoutDestination!.ToString());
    }

    [Fact]
    public async Task Cache_expires_after_one_hour()
    {
        using var provider = Provider();

        await provider.GetAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(61));
        await provider.GetAsync(CancellationToken.None);

        Assert.Equal(2, _downloads);
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    private sealed class StubFactory(MetadataProviderTests owner) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(owner));
    }

    private sealed class StubHandler(MetadataProviderTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(owner.Metadata()) });
    }
}
