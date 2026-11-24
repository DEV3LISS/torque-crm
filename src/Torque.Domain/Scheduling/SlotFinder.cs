namespace Torque.Domain.Scheduling;

/// <summary>A half-open time interval [Start, End): 10:00–11:00 and 11:00–12:00 do not overlap.</summary>
public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}

/// <summary>
/// Pure function: given the opening window, the busy intervals of one bay and the job length,
/// list every start time on the 15-minute grid where the job fits. No database, easy to unit-test.
/// </summary>
public static class SlotFinder
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<DateTimeOffset> FreeSlots(
        TimeRange openingWindow,
        TimeSpan duration,
        IReadOnlyCollection<TimeRange> busy,
        DateTimeOffset notBefore)
    {
        var slots = new List<DateTimeOffset>();
        for (var start = openingWindow.Start; start + duration <= openingWindow.End; start += Step)
        {
            if (start < notBefore)
            {
                continue;
            }

            var candidate = new TimeRange(start, start + duration);
            if (!busy.Any(candidate.Overlaps))
            {
                slots.Add(start);
            }
        }

        return slots;
    }
}
