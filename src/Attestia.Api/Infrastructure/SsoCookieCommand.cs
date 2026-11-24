using Attestia.Core.Sso;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// Strumento di verifica manuale (checklist Windows): stampa un valore di cookie <c>attestia_sso</c>
/// firmato con la chiave configurata. Uso:
/// <c>dotnet Attestia.Api.dll sso-cookie E0001 Employee [--expires-in SECONDI]</c> (negativo = gia' scaduto).
/// Non e' esposto via HTTP: richiede accesso alla macchina e alla chiave.
/// </summary>
internal static class SsoCookieCommand
{
    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == "sso-cookie";

    public static int Run(IConfiguration configuration, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Uso: sso-cookie <matricola> <Employee|HR|Admin> [--expires-in secondi]");
            return 2;
        }

        var seconds = 3600L;
        var i = Array.IndexOf(args, "--expires-in");
        if (i > 0 && i + 1 < args.Length && !long.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out seconds))
        {
            Console.Error.WriteLine("--expires-in deve essere un numero di secondi.");
            return 2;
        }

        var cookie = SsoCookie.FromBase64Key(configuration[SsoCookie.KeyEnvironmentVariable]);
        Console.WriteLine(cookie.Issue(args[1], args[2], DateTimeOffset.UtcNow.AddSeconds(seconds)));
        return 0;
    }
}
