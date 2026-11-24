using System.Data;
using Dapper;

namespace Attestia.Data.Infrastructure;

/// <summary>
/// Registra una sola volta i type handler necessari: le colonne SQL <c>DATE</c> sono <see cref="DateOnly"/>
/// nel dominio, ma SqlClient le restituisce come <see cref="DateTime"/>.
/// </summary>
public static class DapperSetup
{
    private static int _configured;

    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        SqlMapper.RemoveTypeMap(typeof(DateOnly));
        SqlMapper.RemoveTypeMap(typeof(DateOnly?));
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateTime dt => DateOnly.FromDateTime(dt),
            DateOnly d => d,
            _ => throw new DataException($"Impossibile convertire {value.GetType()} in DateOnly."),
        };
    }
}
