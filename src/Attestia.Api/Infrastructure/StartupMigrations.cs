using Attestia.Data.Migrations;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// Applica le migrazioni all'avvio se <c>Database:MigrateOnStartup</c> e' attivo (default solo in sviluppo).
/// E' un hosted service e non codice in Program.cs: cosi' la generazione OpenAPI a build-time,
/// che costruisce l'host senza avviarlo, non tenta di collegarsi al database.
/// </summary>
internal sealed partial class StartupMigrations(IConfiguration configuration, ILogger<StartupMigrations> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return Task.CompletedTask;
        }

        var cs = configuration.GetConnectionString("Attestia")
            ?? throw new InvalidOperationException("Connection string 'Attestia' mancante.");
        var outcome = DatabaseMigrator.Migrate(cs, includeSeed: configuration.GetValue<bool>("Database:SeedOnStartup"), logger);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException("Migrazioni del database non riuscite: vedere il log.");
        }

        Applied(logger, outcome.AppliedScripts.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrazioni all'avvio completate ({Count} script applicati)")]
    private static partial void Applied(ILogger logger, int count);
}
