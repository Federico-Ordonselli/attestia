namespace Attestia.Core.Errors;

/// <summary>
/// Errori attesi con un messaggio sicuro da mostrare all'utente (in italiano).
/// L'API li traduce in ProblemDetails; qualunque altra eccezione diventa un 500 generico.
/// </summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>Conflitto con lo stato attuale (duplicato, file gia' importato) → 409.</summary>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>Richiesta formalmente valida ma non applicabile (riferimento inesistente, data futura) → 422.</summary>
public sealed class UnprocessableException(string message) : DomainException(message);
