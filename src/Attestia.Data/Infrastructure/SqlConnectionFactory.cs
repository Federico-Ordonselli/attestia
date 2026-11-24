using Microsoft.Data.SqlClient;

namespace Attestia.Data.Infrastructure;

public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        DapperSetup.EnsureConfigured();
        _connectionString = connectionString;
    }

    public SqlConnection Create() => new(_connectionString);
}
