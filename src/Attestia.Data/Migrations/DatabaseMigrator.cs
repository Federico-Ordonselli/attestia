using System.Globalization;
using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Attestia.Data.Migrations;

/// <summary>Risultato di un'esecuzione del runner.</summary>
public sealed record MigrationOutcome(bool Succeeded, IReadOnlyList<string> AppliedScripts, string? Error);

/// <summary>
/// Applica gli script T-SQL versionati di <c>/db/migrations</c> (incorporati nell'assembly) e, se richiesto,
/// il seed dimostrativo di <c>/db/seed</c>. I due insiemi hanno journal separati: il seed non e' mai
/// obbligatorio e non si mescola con lo schema.
/// </summary>
public static partial class DatabaseMigrator
{
    private const string MigrationsPrefix = ".Scripts.Migrations.";
    private const string SeedPrefix = ".Scripts.Seed.";

    public static MigrationOutcome Migrate(string connectionString, bool includeSeed, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        var log = new UpgradeLogAdapter(logger);
        EnsureDatabase.For.SqlDatabase(connectionString, log);

        var applied = new List<string>();
        var schema = Run(connectionString, MigrationsPrefix, "SchemaVersions", log);
        applied.AddRange(schema.Scripts.Select(s => s.Name));
        if (!schema.Successful)
        {
            return new MigrationOutcome(false, applied, schema.Error?.Message);
        }

        if (includeSeed)
        {
            var seed = Run(connectionString, SeedPrefix, "SeedVersions", log);
            applied.AddRange(seed.Scripts.Select(s => s.Name));
            if (!seed.Successful)
            {
                return new MigrationOutcome(false, applied, seed.Error?.Message);
            }
        }

        return new MigrationOutcome(true, applied, null);
    }

    private static DatabaseUpgradeResult Run(string connectionString, string prefix, string journalTable, IUpgradeLog log)
    {
        var engine = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.Contains(prefix, StringComparison.Ordinal))
            .JournalToSqlTable("dbo", journalTable)
            .WithTransactionPerScript()
            .WithExecutionTimeout(TimeSpan.FromMinutes(2))
            .LogTo(log)
            .Build();

        return engine.PerformUpgrade();
    }

    /// <summary>Instrada il log di DbUp nel logging strutturato di .NET.</summary>
    private sealed partial class UpgradeLogAdapter(ILogger logger) : IUpgradeLog
    {
        private static string F(string format, object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

        public void LogTrace(string format, params object[] args)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                Trace(logger, F(format, args));
            }
        }

        public void LogDebug(string format, params object[] args)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                Debug(logger, F(format, args));
            }
        }

        public void LogInformation(string format, params object[] args)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                Info(logger, F(format, args));
            }
        }

        public void LogWarning(string format, params object[] args)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                Warn(logger, F(format, args));
            }
        }

        public void LogError(string format, params object[] args) => Error(logger, null, F(format, args));
        public void LogError(Exception ex, string format, params object[] args) => Error(logger, ex, F(format, args));

        [LoggerMessage(Level = LogLevel.Trace, Message = "{DbUpMessage}")]
        private static partial void Trace(ILogger logger, string dbUpMessage);

        [LoggerMessage(Level = LogLevel.Debug, Message = "{DbUpMessage}")]
        private static partial void Debug(ILogger logger, string dbUpMessage);

        [LoggerMessage(Level = LogLevel.Information, Message = "{DbUpMessage}")]
        private static partial void Info(ILogger logger, string dbUpMessage);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{DbUpMessage}")]
        private static partial void Warn(ILogger logger, string dbUpMessage);

        [LoggerMessage(Level = LogLevel.Error, Message = "{DbUpMessage}")]
        private static partial void Error(ILogger logger, Exception? exception, string dbUpMessage);
    }
}
