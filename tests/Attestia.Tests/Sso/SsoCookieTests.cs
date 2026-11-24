using System.Security.Cryptography;
using System.Text;
using Attestia.Core.Sso;

namespace Attestia.Tests.Sso;

public sealed class SsoCookieTests
{
    /// <summary>
    /// Vettore di test calcolato con Python (hmac/hashlib), indipendente da .NET. Lo stesso vettore e' nella
    /// checklist Windows per verificare l'implementazione VBScript di legacy/portale/include/auth.asp.
    /// </summary>
    public const string VectorKeyBase64 = "YXR0ZXN0aWEtdGVzdC12ZWN0b3Ita2V5LTAxMjM0NTY3ODlhYmNkZWY=";
    public const string VectorCookie = "v1|E0001|Employee|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d";

    private static readonly SsoCookie Cookie = SsoCookie.FromBase64Key(VectorKeyBase64);
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Matches_the_independent_test_vector()
    {
        Assert.Equal(VectorCookie, Cookie.Issue("E0001", "Employee", DateTimeOffset.FromUnixTimeSeconds(1893456000)));
        Assert.True(Cookie.Validate(VectorCookie, Now).IsValid);
    }

    [Fact]
    public void Round_trip_returns_the_session()
    {
        var value = Cookie.Issue("E0042", "HR", Now.AddMinutes(60));

        var result = Cookie.Validate(value, Now);

        Assert.Equal(new SsoSession("E0042", "HR", DateTimeOffset.FromUnixTimeSeconds(Now.AddMinutes(60).ToUnixTimeSeconds())), result.Session);
        Assert.Matches("^[A-Za-z0-9|-]+$", value); // nessun carattere che richieda codifica URL
    }

    [Theory]
    [InlineData("v1|E0001|Admin|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d")]    // ruolo elevato
    [InlineData("v1|E0002|Employee|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d")] // altra matricola
    [InlineData("v1|E0001|Employee|1993456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d")] // scadenza allungata
    [InlineData("v1|E0001|Employee|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791e")] // firma alterata
    [InlineData("v1|E0001|Employee|1893456000|BF8110B684F1A1469583295C22AD83FAB40E113E4CC08F58103E9217D0EF791D")] // esadecimale maiuscolo
    public void Any_tampering_invalidates_the_signature(string tampered) =>
        Assert.Equal(SsoCookieError.BadSignature, Cookie.Validate(tampered, Now).Error);

    [Fact]
    public void Expired_cookie_is_rejected_even_with_a_valid_signature()
    {
        var value = Cookie.Issue("E0001", "Employee", Now.AddSeconds(-1));

        Assert.Equal(SsoCookieError.Expired, Cookie.Validate(value, Now).Error);
        Assert.Equal(SsoCookieError.Expired, Cookie.Validate(Cookie.Issue("E0001", "Employee", Now), Now).Error); // exp == now
    }

    [Fact]
    public void A_cookie_signed_with_another_key_is_rejected()
    {
        var other = new SsoCookie(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(SsoCookieError.BadSignature, Cookie.Validate(other.Issue("E0001", "HR", Now.AddHours(1)), Now).Error);
    }

    [Fact]
    public void A_correctly_signed_but_unknown_role_is_rejected()
    {
        var key = Convert.FromBase64String(VectorKeyBase64);
        const string payload = "v1|E0001|Root|1893456000";
        var forged = payload + "|" + Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

        Assert.Equal(SsoCookieError.InvalidRole, Cookie.Validate(forged, Now).Error);
    }

    [Theory]
    [InlineData(null, SsoCookieError.Missing)]
    [InlineData("", SsoCookieError.Missing)]
    [InlineData("v1|E0001|Employee|1893456000", SsoCookieError.Malformed)]
    [InlineData("v1%7CE0001%7CEmployee%7C1893456000%7Cbf81", SsoCookieError.Malformed)]  // valore URL-encoded
    [InlineData("v1|e0001|Employee|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d", SsoCookieError.Malformed)]
    [InlineData("v1|E0001|Employee|-5|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d", SsoCookieError.Malformed)]
    [InlineData("v2|E0001|Employee|1893456000|bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d", SsoCookieError.UnsupportedVersion)]
    [InlineData("v1|E0001|Employee|1893456000|bf81", SsoCookieError.Malformed)]
    public void Malformed_values_are_rejected(string? value, SsoCookieError expected) =>
        Assert.Equal(expected, Cookie.Validate(value, Now).Error);

    [Theory]
    [InlineData(null)]
    [InlineData("non-base64!")]
    [InlineData("c2hvcnQ=")] // "short": meno di 32 byte
    public void Keys_must_be_base64_and_at_least_32_bytes(string? key) =>
        Assert.Throws<InvalidOperationException>(() => SsoCookie.FromBase64Key(key));

    [Theory]
    [InlineData("e0001", "Employee")]
    [InlineData("E0001|HR", "Employee")]
    [InlineData("E0001", "Root")]
    public void Issue_refuses_values_that_would_break_the_format(string number, string role) =>
        Assert.Throws<ArgumentException>(() => Cookie.Issue(number, role, Now.AddHours(1)));
}
