using Torque.Domain.Common;

namespace Torque.Domain.Scheduling;

/// <summary>
/// Answers "when is the shop open?" in absolute time. Timestamps are stored in UTC, but opening hours are
/// local ("09:00 in Yekaterinburg"), so every check converts through the business time zone.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly Dictionary<DayOfWeek, WorkingHours> _hours;

    public BusinessCalendar(TimeZoneInfo timeZone, IEnumerable<WorkingHours> hours)
    {
        TimeZone = timeZone;
        _hours = hours.ToDictionary(h => h.Day);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    /// <summary>Opening and closing instants for a local date, or null if closed that day.</summary>
    public (DateTimeOffset Opens, DateTimeOffset Closes)? OpeningWindow(DateOnly date)
    {
        if (!_hours.TryGetValue(date.DayOfWeek, out var day) || day.IsClosed)
        {
            return null;
        }

        return (ToInstant(date, day.Opens), ToInstant(date, day.Closes));
    }

    public void EnsureOpen(DateTimeOffset start, DateTimeOffset end)
    {
        var window = OpeningWindow(LocalDate(start));
        if (window is null || start < window.Value.Opens || end > window.Value.Closes)
        {
            throw new DomainException("The appointment must be within working hours.");
        }
    }

    private DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TimeZone), TimeSpan.Zero);
    }
}
