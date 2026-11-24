using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Application.Messaging;
using Torque.Domain.Common;
using Torque.Domain.Scheduling;

namespace Torque.Application.Scheduling;

/// <summary>
/// Booking. The "no overlap in a bay" rule is guaranteed by the database (exclusion constraint),
/// so even two requests at the very same millisecond cannot both succeed: the loser gets 409.
/// The checks here exist to give clear error messages and to validate everything else.
/// </summary>
public sealed class AppointmentService(
    IAppDbContext db,
    TimeProvider clock,
    BusinessCalendarProvider calendars,
    TorqueMetrics metrics,
    IValidator<AvailabilityQuery> availabilityValidator,
    IValidator<BookAppointmentRequest> bookValidator,
    IValidator<RescheduleRequest> rescheduleValidator,
    IValidator<AppointmentListQuery> listValidator)
{
    public async Task<AvailabilityDto> GetAvailabilityAsync(AvailabilityQuery query, CancellationToken ct)
    {
        await availabilityValidator.ValidateAndThrowAsync(query, ct);
        var services = await LoadServicesAsync(query.ServiceIds, ct);
        var duration = TimeSpan.FromMinutes(services.Sum(s => s.DurationMinutes));

        var calendar = await calendars.GetAsync(ct);
        var window = calendar.OpeningWindow(query.Date);
        if (window is null)
        {
            return new AvailabilityDto(query.Date, (int)duration.TotalMinutes, []);
        }

        var (opens, closes) = window.Value;
        var bays = await db.Bays.AsNoTracking()
            .Where(b => b.IsActive && (query.BayId == null || b.Id == query.BayId))
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
        var bayIds = bays.Select(b => b.Id).ToList();

        // One query for the whole day, all bays — then group in memory.
        var earliestStart = opens - Appointment.MaxDuration;
        var busy = await db.Appointments.AsNoTracking()
            .Where(a => bayIds.Contains(a.BayId)
                && Appointment.ActiveStatuses.Contains(a.Status)
                && a.StartsAt > earliestStart && a.StartsAt < closes && a.EndsAt > opens)
            .Select(a => new { a.BayId, a.StartsAt, a.EndsAt })
            .ToListAsync(ct);
        var busyByBay = busy.ToLookup(a => a.BayId, a => new TimeRange(a.StartsAt, a.EndsAt));

        var now = clock.GetUtcNow();
        var result = bays
            .Select(b => new BaySlotsDto(b.Id, b.Name,
                SlotFinder.FreeSlots(new TimeRange(opens, closes), duration, busyByBay[b.Id].ToList(), now)))
            .ToList();

        return new AvailabilityDto(query.Date, (int)duration.TotalMinutes, result);
    }

    public async Task<AppointmentDto> BookAsync(BookAppointmentRequest request, CancellationToken ct)
    {
        await bookValidator.ValidateAndThrowAsync(request, ct);

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);
        if (customer.IsArchived)
        {
            throw new DomainException("Cannot book for an archived customer.");
        }

        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == request.VehicleId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);
        if (vehicle.CustomerId != customer.Id)
        {
            throw new DomainException("The vehicle does not belong to this customer.");
        }

        await EnsureBayActiveAsync(request.BayId, ct);
        var services = await LoadServicesAsync(request.ServiceIds, ct);

        var appointment = Appointment.Schedule(
            customer.Id,
            vehicle.Id,
            request.BayId,
            request.StartsAt,
            services.Select(s => new BookedService(s.Id, s.DurationMinutes)).ToList(),
            request.Comment,
            clock.GetUtcNow(),
            await calendars.GetAsync(ct));

        db.Appointments.Add(appointment);
        AppointmentReminder.Schedule(db, appointment, clock.GetUtcNow()); // same transaction as the booking
        await db.SaveChangesAsync(ct); // exclusion constraint violation → 409 "Time slot taken"
        metrics.AppointmentBooked();
        return await GetAsync(appointment.Id, ct);
    }

    public async Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct)
    {
        await rescheduleValidator.ValidateAndThrowAsync(request, ct);
        var appointment = await LoadAsync(id, ct);
        await EnsureBayActiveAsync(request.BayId, ct);

        var now = clock.GetUtcNow();
        appointment.Reschedule(request.BayId, request.StartsAt, now, await calendars.GetAsync(ct));
        AppointmentReminder.Schedule(db, appointment, now); // the old reminder will notice the new time and skip itself
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public Task<AppointmentDto> CheckInAsync(Guid id, CancellationToken ct) => ChangeAsync(id, a => a.CheckIn(), ct);

    public Task<AppointmentDto> CompleteAsync(Guid id, CancellationToken ct) => ChangeAsync(id, a => a.Complete(), ct);

    public Task<AppointmentDto> CancelAsync(Guid id, CancelRequest request, CancellationToken ct) =>
        ChangeAsync(id, a => a.Cancel(request.Reason), ct);

    public Task<AppointmentDto> MarkNoShowAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, a => a.MarkNoShow(clock.GetUtcNow()), ct);

    public async Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct) =>
        (await Project(db.Appointments.AsNoTracking().Where(a => a.Id == id)).SingleOrDefaultAsync(ct))
        ?? throw new NotFoundException("Appointment", id);

    /// <summary>The schedule board: all appointments in a period, optionally for one bay or status.</summary>
    public async Task<IReadOnlyList<AppointmentDto>> ListAsync(AppointmentListQuery query, CancellationToken ct)
    {
        await listValidator.ValidateAndThrowAsync(query, ct);
        var from = query.From.ToUniversalTime(); // query parameters must be UTC for timestamptz
        var to = query.To.ToUniversalTime();
        var earliestStart = from - Appointment.MaxDuration; // index lower bound, see Appointment.MaxDuration
        var appointments = db.Appointments.AsNoTracking()
            .Where(a => a.StartsAt > earliestStart && a.StartsAt < to && a.EndsAt > from);
        if (query.BayId is not null)
        {
            appointments = appointments.Where(a => a.BayId == query.BayId);
        }

        if (query.Status is not null)
        {
            appointments = appointments.Where(a => a.Status == query.Status);
        }

        return await Project(appointments.OrderBy(a => a.StartsAt)).ToListAsync(ct);
    }

    private async Task<AppointmentDto> ChangeAsync(Guid id, Action<Appointment> change, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        change(appointment);
        await db.SaveChangesAsync(ct); // optimistic concurrency (xmin): two parallel status changes → 409
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Builds the DTO in SQL with joins — one round-trip, only the needed columns, no N+1 queries.
    /// </summary>
    private IQueryable<AppointmentDto> Project(IQueryable<Appointment> appointments) =>
        from a in appointments
        join c in db.Customers on a.CustomerId equals c.Id
        join v in db.Vehicles on a.VehicleId equals v.Id
        join b in db.Bays on a.BayId equals b.Id
        select new AppointmentDto(
            a.Id, c.Id, c.FullName, c.Phone, v.Id, v.Plate, v.Make + " " + v.Model, b.Id, b.Name,
            a.StartsAt, a.EndsAt, a.Status, a.Services.Select(s => s.ServiceId).ToList(), a.Comment, a.CancellationReason);

    private async Task<Appointment> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Appointment", id);

    private async Task EnsureBayActiveAsync(Guid bayId, CancellationToken ct)
    {
        var bay = await db.Bays.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bayId, ct)
            ?? throw new NotFoundException("Bay", bayId);
        if (!bay.IsActive)
        {
            throw new DomainException("The bay is not in use.");
        }
    }

    private async Task<List<Domain.Catalog.ServiceOffering>> LoadServicesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var services = await db.Services.AsNoTracking().Where(s => distinct.Contains(s.Id)).ToListAsync(ct);

        var missing = distinct.Except(services.Select(s => s.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Service", string.Join(", ", missing));
        }

        if (services.Any(s => !s.IsActive))
        {
            throw new DomainException("One or more services are no longer offered.");
        }

        if (distinct.Count != ids.Count)
        {
            throw new DomainException("A service can be booked only once per appointment.");
        }

        return services;
    }
}
