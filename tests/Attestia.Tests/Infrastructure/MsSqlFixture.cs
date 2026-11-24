using Attestia.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Attestia.Tests.Infrastructure.MsSqlFixture))]

namespace Attestia.Tests.Infrastructure;

/// <summary>
/// Un SQL Server 2022 reale in Docker, condiviso da tutta l'assembly di test.
/// Ogni classe di test chiede un proprio database (<see cref="CreateDatabaseAsync"/>) migrato da zero:
/// i test sono isolati senza pagare l'avvio di un container per classe.
/// </summary>
public sealed class MsSqlFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Crea un database con nome univoco, applica le migrazioni (ed eventualmente il seed) e ne restituisce la connection string.</summary>
    public string CreateDatabase(string prefix, bool seed = false)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 120)],
            TrustServerCertificate = true,
        };
        var outcome = DatabaseMigrator.Migrate(builder.ConnectionString, seed, NullLogger.Instance);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException($"Migrazione fallita: {outcome.Error}");
        }

        return builder.ConnectionString;
    }
}
