using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Attestia.Core.Security;

namespace Attestia.Core.Sso;

public sealed record SsoSession(string EmployeeNumber, string Role, DateTimeOffset ExpiresAt);

public enum SsoCookieError
{
    None,
    Missing,
    Malformed,
    UnsupportedVersion,
    BadSignature,
    Expired,
    InvalidRole,
}

public readonly record struct SsoCookieValidation(SsoSession? Session, SsoCookieError Error)
{
    public bool IsValid => Session is not null;
}

/// <summary>
/// Cookie di sessione condiviso tra API .NET e portale ASP classic (che non sa validare SAML).
/// Formato: <c>v1|{employeeNumber}|{role}|{exp}|{sig}</c>
/// <list type="bullet">
/// <item><c>exp</c>: secondi Unix UTC;</item>
/// <item><c>sig</c>: HMAC-SHA256 esadecimale minuscolo dei byte UTF-8 di <c>v1|{employeeNumber}|{role}|{exp}</c>;</item>
/// <item>chiave: base64 di almeno 32 byte, variabile d'ambiente <c>ATTESTIA_SSO_KEY</c>.</item>
/// </list>
/// L'alfabeto del valore e' ristretto a <c>[A-Za-z0-9|-]</c>: nessuna codifica URL, cosi' ASP classic legge
/// esattamente i byte firmati. Il contratto e' verificato anche da <c>scripts/check_portal.py</c>.
/// </summary>
public sealed partial class SsoCookie
{
    public const string CookieName = "attestia_sso";
    public const string Version = "v1";
    public const string KeyEnvironmentVariable = "ATTESTIA_SSO_KEY";
    public const int MinimumKeyBytes = 32;

    private readonly byte[] _key;

    public SsoCookie(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < MinimumKeyBytes)
        {
            throw new ArgumentException($"La chiave SSO deve essere di almeno {MinimumKeyBytes} byte.", nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    /// <summary>Decodifica la chiave base64 (es. da <c>ATTESTIA_SSO_KEY</c>); errore chiaro se manca o e' troppo corta.</summary>
    public static SsoCookie FromBase64Key(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException($"Variabile {KeyEnvironmentVariable} mancante: impossibile emettere o verificare la sessione SSO.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{KeyEnvironmentVariable} non è un valore base64 valido.", ex);
        }

        return key.Length < MinimumKeyBytes
            ? throw new InvalidOperationException($"{KeyEnvironmentVariable} deve contenere almeno {MinimumKeyBytes} byte (base64 di {MinimumKeyBytes}+ byte).")
            : new SsoCookie(key);
    }

    public string Issue(string employeeNumber, string role, DateTimeOffset expiresAt)
    {
        if (!EmployeeNumberPattern().IsMatch(employeeNumber ?? ""))
        {
            throw new ArgumentException("Matricola non valida per il cookie SSO.", nameof(employeeNumber));
        }

        if (!Roles.IsValid(role))
        {
            throw new ArgumentException("Ruolo non valido per il cookie SSO.", nameof(role));
        }

        var payload = string.Join('|', Version, employeeNumber, role, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return payload + "|" + Sign(payload);
    }

    public SsoCookieValidation Validate(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new(null, SsoCookieError.Missing);
        }

        if (value.Length > 200 || !CookieAlphabet().IsMatch(value) || value.Split('|') is not [var version, var number, var role, var exp, var sig])
        {
            return new(null, SsoCookieError.Malformed);
        }

        if (version != Version)
        {
            return new(null, SsoCookieError.UnsupportedVersion);
        }

        if (!EmployeeNumberPattern().IsMatch(number) || !ExpiryPattern().IsMatch(exp) || sig.Length != 64)
        {
            return new(null, SsoCookieError.Malformed);
        }

        // La firma si verifica PRIMA di interpretare il contenuto, e a tempo costante.
        var expected = Encoding.ASCII.GetBytes(Sign(string.Join('|', version, number, role, exp)));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(sig)))
        {
            return new(null, SsoCookieError.BadSignature);
        }

        if (!Roles.IsValid(role))
        {
            return new(null, SsoCookieError.InvalidRole);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp, CultureInfo.InvariantCulture));
        return expiresAt <= now ? new(null, SsoCookieError.Expired) : new(new SsoSession(number, role, expiresAt), SsoCookieError.None);
    }

    private string Sign(string payload) => Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));

    [GeneratedRegex("^[A-Z0-9-]{2,20}$")]
    private static partial Regex EmployeeNumberPattern();

    [GeneratedRegex("^[0-9]{1,10}$")]
    private static partial Regex ExpiryPattern();

    [GeneratedRegex("^[A-Za-z0-9|-]+$")]
    private static partial Regex CookieAlphabet();
}
