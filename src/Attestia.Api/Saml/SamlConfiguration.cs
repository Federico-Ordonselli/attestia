using System.Security.Cryptography.X509Certificates;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.IdentityModel.Tokens;

namespace Attestia.Api.Saml;

public sealed class SamlOptions
{
    /// <summary>EntityID del Service Provider (deve coincidere con il clientId del client SAML in Keycloak).</summary>
    public string EntityId { get; set; } = "urn:attestia:sp";

    /// <summary>Origine pubblica del sito (es. https://attestia.local): base di /sso/acs e /sso/logout.</summary>
    public string BaseUrl { get; set; } = "https://localhost:7180";

    /// <summary>URL dei metadata dell'IdP (Keycloak: /realms/{realm}/protocol/saml/descriptor).</summary>
    public string? IdpMetadataUrl { get; set; }

    /// <summary>Durata del cookie attestia_sso (breve: l'IdP mantiene la sessione e il rinnovo e' trasparente).</summary>
    public int SessionMinutes { get; set; } = 60;

    public Uri AcsUrl => new(new Uri(BaseUrl), "/sso/acs");

    public Uri LogoutUrl => new(new Uri(BaseUrl), "/sso/logout");
}

/// <summary>Configurazione ITfoxtec dell'SP; quella completa (certificati IdP) e' caricata dai metadata alla prima richiesta.</summary>
public interface ISamlConfigurationProvider
{
    /// <summary>Configurazione dell'SP senza dati dell'IdP: basta per esporre i metadata.</summary>
    Saml2Configuration ServiceProvider { get; }

    Task<Saml2Configuration> GetAsync(CancellationToken ct);

    /// <summary>
    /// Scarta la configurazione in cache (es. firma non valida dopo una rotazione delle chiavi dell'IdP).
    /// Restituisce false se l'ultima lettura e' troppo recente: un attaccante non puo' forzare ricaricamenti continui.
    /// </summary>
    bool TryInvalidate();
}

/// <summary>
/// Legge i metadata dell'IdP in modo lazy (non all'avvio: l'API parte anche se Keycloak non e' ancora pronto,
/// e la generazione OpenAPI a build-time non tenta connessioni) e li tiene in cache per un'ora.
/// </summary>
internal sealed class MetadataSamlConfigurationProvider(SamlOptions options, IHttpClientFactory httpClientFactory, TimeProvider time)
    : ISamlConfigurationProvider, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumReloadInterval = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Saml2Configuration? _cached;
    private DateTimeOffset _loadedAt;

    public Saml2Configuration ServiceProvider => CreateBase(options);

    public async Task<Saml2Configuration> GetAsync(CancellationToken ct)
    {
        if (_cached is not null && time.GetUtcNow() - _loadedAt < CacheDuration)
        {
            return _cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null && time.GetUtcNow() - _loadedAt < CacheDuration)
            {
                return _cached;
            }

            if (string.IsNullOrWhiteSpace(options.IdpMetadataUrl))
            {
                throw new InvalidOperationException("Saml:IdpMetadataUrl non configurato.");
            }

            var descriptor = await new EntityDescriptor().ReadIdPSsoDescriptorFromUrlAsync(httpClientFactory, new Uri(options.IdpMetadataUrl), ct);
            var idp = descriptor.IdPSsoDescriptor ?? throw new InvalidOperationException("I metadata non descrivono un IdP SAML.");
            _cached = Complete(CreateBase(options), descriptor.EntityId,
                idp.SingleSignOnServices.First(s => s.Binding == ITfoxtec.Identity.Saml2.Schemas.ProtocolBindings.HttpRedirect).Location,
                idp.SingleLogoutServices.FirstOrDefault(s => s.Binding == ITfoxtec.Identity.Saml2.Schemas.ProtocolBindings.HttpRedirect)?.Location,
                idp.SigningCertificates);
            _loadedAt = time.GetUtcNow();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool TryInvalidate()
    {
        if (_cached is null || time.GetUtcNow() - _loadedAt < MinimumReloadInterval)
        {
            return false;
        }

        _cached = null;
        return true;
    }

    internal static Saml2Configuration CreateBase(SamlOptions options)
    {
        var config = new Saml2Configuration
        {
            Issuer = options.EntityId,
            SignatureAlgorithm = SecurityAlgorithms.RsaSha256Signature,
            // La fiducia e' nel certificato pubblicato dai metadata dell'IdP (pinning), non in una CA:
            // Keycloak usa un certificato autofirmato. La FIRMA resta obbligatoria e verificata.
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
            AudienceRestricted = true,
            DetectReplayedTokens = true,
        };
        config.AllowedAudienceUris.Add(options.EntityId);
        return config;
    }

    internal static Saml2Configuration Complete(Saml2Configuration config, string idpEntityId, Uri ssoUrl, Uri? sloUrl, IEnumerable<X509Certificate2> signingCertificates)
    {
        config.AllowedIssuer = idpEntityId;
        config.SingleSignOnDestination = ssoUrl;
        config.SingleLogoutDestination = sloUrl;
        config.SignatureValidationCertificates.AddRange(signingCertificates);
        if (config.SignatureValidationCertificates.Count == 0)
        {
            throw new InvalidOperationException("Nessun certificato di firma nei metadata dell'IdP.");
        }

        return config;
    }

    public void Dispose() => _lock.Dispose();
}
