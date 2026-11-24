using Torque.Domain.Common;
using Torque.Domain.Scheduling;

namespace Torque.UnitTests.Domain;

/// <summary>Shared setup: Yekaterinburg (UTC+5), open Mon–Sat 09:00–20:00, Sunday closed.</summary>
public abstract class SchedulingTestBase
{
    protected static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yekaterinburg");
    protected static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    protected static readonly BusinessCalendar Calendar = new(Zone,
        Enum.GetValues<DayOfWeek>().Select(d => d == DayOfWeek.Sunday
            ? WorkingHours.Closed(d)
            : WorkingHours.Open(d, new TimeOnly(9, 0), new TimeOnly(20, 0))));

    // Monday 1 Dec 2026, local time.
    protected static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 12, day, hour, minute, 0, Offset);

    protected static readonly DateTimeOffset Now = Local(1, 8);
}

public class BusinessCalendarTests : SchedulingTestBase
{
    [Fact]
    public void Opening_window_is_converted_from_local_time_to_utc()
    {
        var window = Calendar.OpeningWindow(new DateOnly(2026, 12, 1))!.Value;

        window.Opens.ShouldBe(new DateTimeOffset(2026, 12, 1, 4, 0, 0, TimeSpan.Zero)); // 09:00 +05 = 04:00 UTC
        window.Closes.ShouldBe(new DateTimeOffset(2026, 12, 1, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Closed_day_has_no_window() =>
        Calendar.OpeningWindow(new DateOnly(2026, 12, 6)).ShouldBeNull(); // Sunday

    [Fact]
    public void Local_date_uses_business_time_zone()
    {
        // 22:00 UTC on 30 Nov is already 03:00 on 1 Dec in Yekaterinburg.
        Calendar.LocalDate(new DateTimeOffset(2026, 11, 30, 22, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 12, 1));
    }
}

public class SlotFinderTests : SchedulingTestBase
{
    private static readonly TimeRange Day = new(Local(1, 9), Local(1, 12));

    [Fact]
    public void Empty_bay_offers_every_15_minutes_until_the_job_still_fits()
    {
        var slots = SlotFinder.FreeSlots(Day, TimeSpan.FromMinutes(60), [], Now);

        slots[0].ShouldBe(Local(1, 9));
        slots[^1].ShouldBe(Local(1, 11)); // 11:00–12:00 is the last that fits
        slots.Count.ShouldBe(9);
    }

    [Fact]
    public void Busy_interval_blocks_overlapping_starts_but_not_adjacent_ones()
    {
        TimeRange[] busy = [new(Local(1, 10), Local(1, 10, 30))];

        var slots = SlotFinder.FreeSlots(Day, TimeSpan.FromMinutes(30), busy, Now);

        slots.ShouldContain(Local(1, 9, 30));    // 09:30–10:00 ends exactly when busy starts
        slots.ShouldNotContain(Local(1, 9, 45)); // 09:45–10:15 overlaps
        slots.ShouldNotContain(Local(1, 10, 15));
        slots.ShouldContain(Local(1, 10, 30));   // starts exactly when busy ends
    }

    [Fact]
    public void Past_slots_are_not_offered()
    {
        var slots = SlotFinder.FreeSlots(Day, TimeSpan.FromMinutes(15), [], notBefore: Local(1, 10, 5));

        slots[0].ShouldBe(Local(1, 10, 15));
    }
}

public class AppointmentTests : SchedulingTestBase
{
    private static Appointment Book(DateTimeOffset start, int minutes = 30) =>
        Appointment.Schedule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start,
            [new BookedService(Guid.NewGuid(), minutes)], null, Now, Calendar);

    [Fact]
    public void Duration_is_the_sum_of_services()
    {
        var a = Appointment.Schedule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Local(1, 10),
            [new BookedService(Guid.NewGuid(), 30), new BookedService(Guid.NewGuid(), 45)], " note ", Now, Calendar);

        a.EndsAt.ShouldBe(Local(1, 11, 15));
        a.Status.ShouldBe(AppointmentStatus.Scheduled);
        a.Comment.ShouldBe("note");
        a.Services.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(1, 7, 0)]    // before opening
    [InlineData(1, 19, 45)]  // would end at 20:15, after closing
    [InlineData(6, 10, 0)]   // Sunday
    public void Must_be_within_working_hours(int day, int hour, int minute) =>
        Should.Throw<DomainException>(() => Book(Local(day, hour, minute)));

    [Fact]
    public void Local_time_is_stored_as_utc()
    {
        var a = Book(Local(1, 10)); // 10:00+05:00

        a.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        a.StartsAt.ShouldBe(new DateTimeOffset(2026, 12, 1, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Cannot_book_in_the_past() =>
        Should.Throw<DomainException>(() => Book(Local(1, 7, 45).AddDays(-1)));

    [Fact]
    public void Start_must_be_on_the_grid() =>
        Should.Throw<DomainException>(() => Book(Local(1, 10, 10)));

    [Fact]
    public void Same_service_twice_is_rejected() =>
        Should.Throw<DomainException>(() => Appointment.Schedule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Local(1, 10),
            [new BookedService(Guid.Empty, 30), new BookedService(Guid.Empty, 30)], null, Now, Calendar));

    [Fact]
    public void Happy_path_lifecycle()
    {
        var a = Book(Local(1, 10));

        a.CheckIn();
        a.Complete();

        a.Status.ShouldBe(AppointmentStatus.Completed);
        a.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Cannot_complete_without_check_in() =>
        Should.Throw<InvalidStateException>(() => Book(Local(1, 10)).Complete());

    [Fact]
    public void Cannot_cancel_a_completed_appointment()
    {
        var a = Book(Local(1, 10));
        a.CheckIn();
        a.Complete();

        Should.Throw<InvalidStateException>(() => a.Cancel("changed mind"));
    }

    [Fact]
    public void No_show_only_after_start_time()
    {
        var a = Book(Local(1, 10));

        Should.Throw<DomainException>(() => a.MarkNoShow(Local(1, 9, 59)));
        a.MarkNoShow(Local(1, 10, 5));
        a.Status.ShouldBe(AppointmentStatus.NoShow);
    }

    [Fact]
    public void Reschedule_keeps_duration_and_checks_rules()
    {
        var a = Book(Local(1, 10), minutes: 45);
        var newBay = Guid.NewGuid();

        a.Reschedule(newBay, Local(2, 15), Now, Calendar);

        a.BayId.ShouldBe(newBay);
        a.EndsAt.ShouldBe(Local(2, 15, 45));
        Should.Throw<DomainException>(() => a.Reschedule(newBay, Local(2, 19, 30), Now, Calendar));
    }
}
