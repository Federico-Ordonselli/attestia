namespace Attestia.Api.Infrastructure;

internal static class RateLimits
{
    /// <summary>Upload di file XML: 5 richieste al minuto per utente.</summary>
    public const string Upload = "upload";
}
