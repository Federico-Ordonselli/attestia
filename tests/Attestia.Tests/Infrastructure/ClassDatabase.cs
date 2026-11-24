using Attestia.Data.Infrastructure;

namespace Attestia.Tests.Infrastructure;

/// <summary>Database migrato da zero, condiviso dai test di una singola classe (IClassFixture).</summary>
public sealed class ClassDatabase
{
    public ClassDatabase(MsSqlFixture server)
    {
        DapperSetup.EnsureConfigured();
        Server = server;
        ConnectionString = server.CreateDatabase("t");
    }

    public MsSqlFixture Server { get; }

    public string ConnectionString { get; }
}
