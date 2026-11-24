using Attestia.Core.Errors;
using Microsoft.Data.SqlClient;

namespace Attestia.Data.Infrastructure;

/// <summary>Traduce gli errori SQL attesi (THROW 5xxxx delle procedure, vincoli unique) in eccezioni di dominio.</summary>
public static class SqlErrors
{
    public const int UniqueConstraint = 2627;
    public const int UniqueIndex = 2601;
    public const int CourseCodeExists = 51001;
    public const int EmployeeNotFound = 51002;
    public const int CourseNotFound = 51003;
    public const int ImportReferenceChanged = 51004;

    public static bool IsUniqueViolation(SqlException ex) => ex.Number is UniqueConstraint or UniqueIndex;

    /// <summary>Esegue <paramref name="action"/> traducendo gli errori noti; gli altri risalgono invariati.</summary>
    public static async Task<T> Translate<T>(Func<Task<T>> action, string? duplicateMessage = null)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number == CourseCodeExists)
        {
            throw new ConflictException("Esiste già un corso con questo codice.");
        }
        catch (SqlException ex) when (ex.Number == EmployeeNotFound)
        {
            throw new UnprocessableException("Dipendente inesistente o non attivo.");
        }
        catch (SqlException ex) when (ex.Number == CourseNotFound)
        {
            throw new UnprocessableException("Corso inesistente o disattivato.");
        }
        catch (SqlException ex) when (IsUniqueViolation(ex) && duplicateMessage is not null)
        {
            throw new ConflictException(duplicateMessage);
        }
    }
}
