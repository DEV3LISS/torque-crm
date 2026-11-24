using Torque.Domain.Common;

namespace Torque.Domain.Scheduling;

public enum AppointmentStatus
{
    Scheduled,
    CheckedIn,
    Completed,
    Cancelled,
    NoShow,
}

/// <summary>A service included in an appointment (many-to-many link to the catalog).</summary>
public sealed class AppointmentServiceItem
{
    private AppointmentServiceItem() { }

    internal AppointmentServiceItem(Guid serviceId) => ServiceId = serviceId;

    public Guid AppointmentId { get; private set; }
    public Guid ServiceId { get; private set; }
}

/// <summary>What the appointment is for: the catalog service and its duration at booking time.</summary>
public readonly record struct BookedService(Guid ServiceId, int DurationMinutes);

/// <summary>
/// A booked time slot in a bay. The core invariant — no two active appointments in the same bay overlap —
/// is enforced by a PostgreSQL exclusion constraint; this class enforces everything that does not need
/// to look at other rows (time grid, working hours, not in the past, status transitions).
/// </summary>
public sealed class Appointment : Entity, IHasTimestamps
{
    public const int MaxCommentLength = 1000;

    /// <summary>Statuses that occupy the bay. Must match the WHERE clause of the exclusion constraint.</summary>
    public static readonly AppointmentStatus[] ActiveStatuses = [AppointmentStatus.Scheduled, AppointmentStatus.CheckedIn];

    /// <summary>
    /// An appointment always fits into one working day, so it never lasts longer than this. Queries use it to give
    /// the starts_at index a lower bound: "overlaps [from, to)" becomes
    /// starts_at &gt; from − MaxDuration AND starts_at &lt; to AND ends_at &gt; from — an index range scan instead of
    /// reading every older row (measured: 10.3 ms → 0.08 ms on 200k rows, see docs/performance.md).
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(1);

    private readonly List<AppointmentServiceItem> _services = [];

    private Appointment() { }

    public Guid CustomerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid BayId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? Comment { get; private set; }
    public string? CancellationReason { get; private set; }
    public IReadOnlyCollection<AppointmentServiceItem> Services => _services;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Appointment Schedule(
        Guid customerId,
        Guid vehicleId,
        Guid bayId,
        DateTimeOffset startsAt,
        IReadOnlyCollection<BookedService> services,
        string? comment,
        DateTimeOffset now,
        BusinessCalendar calendar)
    {
        if (services.Count == 0)
        {
            throw new DomainException("At least one service is required.");
        }

        if (services.Select(s => s.ServiceId).Distinct().Count() != services.Count)
        {
            throw new DomainException("A service can be booked only once per appointment.");
        }

        var appointment = new Appointment
        {
            CustomerId = customerId,
            VehicleId = vehicleId,
            Status = AppointmentStatus.Scheduled,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };
        appointment.SetSlot(bayId, startsAt, TimeSpan.FromMinutes(services.Sum(s => s.DurationMinutes)), now, calendar);
        appointment._services.AddRange(services.Select(s => new AppointmentServiceItem(s.ServiceId)));
        return appointment;
    }

    public TimeSpan Duration => EndsAt - StartsAt;

    public bool IsActive => ActiveStatuses.Contains(Status);

    public void Reschedule(Guid bayId, DateTimeOffset startsAt, DateTimeOffset now, BusinessCalendar calendar)
    {
        EnsureStatus(AppointmentStatus.Scheduled, "reschedule");
        SetSlot(bayId, startsAt, Duration, now, calendar);
    }

    public void CheckIn()
    {
        EnsureStatus(AppointmentStatus.Scheduled, "check in");
        Status = AppointmentStatus.CheckedIn;
    }

    public void Complete()
    {
        EnsureStatus(AppointmentStatus.CheckedIn, "complete");
        Status = AppointmentStatus.Completed;
    }

    public void Cancel(string? reason)
    {
        if (!IsActive)
        {
            throw new InvalidStateException($"Cannot cancel an appointment that is {Status}.");
        }

        Status = AppointmentStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>The customer did not come. Only possible once the start time has passed.</summary>
    public void MarkNoShow(DateTimeOffset now)
    {
        EnsureStatus(AppointmentStatus.Scheduled, "mark as no-show");
        if (now < StartsAt)
        {
            throw new DomainException("Cannot mark a no-show before the appointment start time.");
        }

        Status = AppointmentStatus.NoShow;
    }

    private void SetSlot(Guid bayId, DateTimeOffset startsAt, TimeSpan duration, DateTimeOffset now, BusinessCalendar calendar)
    {
        // Clients may send local time ("10:00+05:00"); the system always stores UTC.
        // Same instant, different representation — PostgreSQL timestamptz only accepts offset 0 from Npgsql.
        startsAt = startsAt.ToUniversalTime();

        if (startsAt < now)
        {
            throw new DomainException("Cannot book in the past.");
        }

        if (startsAt.Second != 0 || startsAt.Millisecond != 0 || startsAt.Minute % 15 != 0)
        {
            throw new DomainException("Start time must be on the 15-minute grid (e.g. 10:00, 10:15).");
        }

        if (duration > MaxDuration)
        {
            throw new DomainException("An appointment cannot be longer than one day.");
        }

        var endsAt = startsAt + duration;
        calendar.EnsureOpen(startsAt, endsAt);

        BayId = bayId;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    private void EnsureStatus(AppointmentStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateException($"Cannot {action} an appointment that is {Status}.");
        }
    }
}
