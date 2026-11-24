using Torque.Domain.Common;

namespace Torque.Domain.Scheduling;

/// <summary>Opening hours for one day of the week, in the business's local time.</summary>
public sealed class WorkingHours
{
    private WorkingHours() { }

    public DayOfWeek Day { get; private set; }
    public TimeOnly Opens { get; private set; }
    public TimeOnly Closes { get; private set; }
    public bool IsClosed { get; private set; }

    public static WorkingHours Open(DayOfWeek day, TimeOnly opens, TimeOnly closes)
    {
        if (closes <= opens)
        {
            throw new DomainException($"{day}: closing time must be after opening time.");
        }

        if (opens.Minute % 15 != 0 || closes.Minute % 15 != 0)
        {
            throw new DomainException($"{day}: hours must be on a 15-minute grid.");
        }

        return new WorkingHours { Day = day, Opens = opens, Closes = closes };
    }

    public static WorkingHours Closed(DayOfWeek day) => new() { Day = day, IsClosed = true };
}
