namespace Attestia.Core.Time;

/// <summary>"Oggi" nel fuso dell'azienda (Europa/Roma), indipendente dal fuso del server.</summary>
public interface IBusinessClock
{
    DateOnly Today { get; }

    DateTimeOffset UtcNow { get; }
}

public sealed class BusinessClock(TimeProvider timeProvider, TimeZoneInfo zone) : IBusinessClock
{
    public const string DefaultTimeZoneId = "Europe/Rome";

    public BusinessClock(TimeProvider timeProvider)
        : this(timeProvider, TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId))
    {
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
