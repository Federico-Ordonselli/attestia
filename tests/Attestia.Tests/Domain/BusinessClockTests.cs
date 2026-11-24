using Attestia.Core.Time;
using Microsoft.Extensions.Time.Testing;

namespace Attestia.Tests.Domain;

public sealed class BusinessClockTests
{
    [Theory]
    [InlineData("2026-06-14T21:59:59Z", "2026-06-14")] // 23:59:59 a Roma (ora legale, UTC+2)
    [InlineData("2026-06-14T22:00:00Z", "2026-06-15")] // mezzanotte a Roma: in UTC e' ancora il 14
    [InlineData("2026-01-14T23:00:00Z", "2026-01-15")] // ora solare, UTC+1
    public void Today_is_computed_in_the_business_time_zone(string utc, string expected)
    {
        var clock = new BusinessClock(new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), clock.Today);
    }
}
