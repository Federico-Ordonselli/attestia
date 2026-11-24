using Attestia.Data.Migrations;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// <c>dotnet run --project src/Attestia.Api -- migrate [--seed]</c>: applica le migrazioni (e il seed) ed esce.
/// Con lo stesso codice l'API puo' migrare all'avvio (Database:MigrateOnStartup).
/// </summary>
internal static partial class MigrateCommand
{
    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == "migrate";

    public static int Run(IConfiguration configuration, string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(l => l.AddSimpleConsole(o => o.SingleLine = true));
        var logger = loggerFactory.CreateLogger("Attestia.Migrations");

        var connectionString = configuration.GetConnectionString("Attestia");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            MissingConnectionString(logger);
            return 2;
        }

        var outcome = DatabaseMigrator.Migrate(connectionString, includeSeed: args.Contains("--seed"), logger);
        Completed(logger, outcome.Succeeded, outcome.AppliedScripts.Count);
        return outcome.Succeeded ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection string 'Attestia' mancante: impostare ConnectionStrings__Attestia.")]
    private static partial void MissingConnectionString(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrazioni terminate. Successo: {Succeeded}, script applicati: {Count}")]
    private static partial void Completed(ILogger logger, bool succeeded, int count);
}
