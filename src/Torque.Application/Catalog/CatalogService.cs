using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Catalog;
using Torque.Domain.Scheduling;

namespace Torque.Application.Catalog;

/// <summary>Reference data the owner configures: services and prices, bays, opening hours.</summary>
public sealed class CatalogService(
    IAppDbContext db,
    TimeProvider clock,
    IValidator<SaveServiceRequest> serviceValidator,
    IValidator<SaveBayRequest> bayValidator,
    IValidator<IReadOnlyList<WorkingDayDto>> weekValidator)
{
    // ---- services ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(bool includeInactive, CancellationToken ct)
    {
        var services = db.Services.AsNoTracking();
        if (!includeInactive)
        {
            services = services.Where(s => s.IsActive);
        }

        return await services.OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Category, s.DurationMinutes, s.Price, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<ServiceDto> CreateServiceAsync(SaveServiceRequest request, CancellationToken ct)
    {
        await serviceValidator.ValidateAndThrowAsync(request, ct);
        var service = ServiceOffering.Create(request.Name, request.Category, request.DurationMinutes, request.Price);
        await EnsureServiceNameFreeAsync(service.Name, null, ct);
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);
        return ServiceDto.From(service);
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid id, SaveServiceRequest request, CancellationToken ct)
    {
        await serviceValidator.ValidateAndThrowAsync(request, ct);
        var service = await LoadServiceAsync(id, ct);
        service.Update(request.Name, request.Category, request.DurationMinutes, request.Price);
        await EnsureServiceNameFreeAsync(service.Name, id, ct);
        await db.SaveChangesAsync(ct);
        return ServiceDto.From(service);
    }

    public async Task<ServiceDto> SetServiceActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var service = await LoadServiceAsync(id, ct);
        if (active)
        {
            service.Activate();
        }
        else
        {
            service.Deactivate();
        }

        await db.SaveChangesAsync(ct);
        return ServiceDto.From(service);
    }

    // ---- bays --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BayDto>> ListBaysAsync(bool includeInactive, CancellationToken ct) =>
        await db.Bays.AsNoTracking()
            .Where(b => includeInactive || b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BayDto(b.Id, b.Name, b.IsActive))
            .ToListAsync(ct);

    public async Task<BayDto> CreateBayAsync(SaveBayRequest request, CancellationToken ct)
    {
        await bayValidator.ValidateAndThrowAsync(request, ct);
        var bay = Bay.Create(request.Name);
        await EnsureBayNameFreeAsync(bay.Name, null, ct);
        db.Bays.Add(bay);
        await db.SaveChangesAsync(ct);
        return BayDto.From(bay);
    }

    public async Task<BayDto> RenameBayAsync(Guid id, SaveBayRequest request, CancellationToken ct)
    {
        await bayValidator.ValidateAndThrowAsync(request, ct);
        var bay = await LoadBayAsync(id, ct);
        bay.Rename(request.Name);
        await EnsureBayNameFreeAsync(bay.Name, id, ct);
        await db.SaveChangesAsync(ct);
        return BayDto.From(bay);
    }

    /// <summary>A bay with upcoming bookings cannot be switched off — those customers would be stranded.</summary>
    public async Task<BayDto> SetBayActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var bay = await LoadBayAsync(id, ct);
        if (!active)
        {
            var hasUpcoming = await db.Appointments.AnyAsync(
                a => a.BayId == id && a.EndsAt > now && Appointment.ActiveStatuses.Contains(a.Status), ct);
            if (hasUpcoming)
            {
                throw new ConflictException("The bay has upcoming appointments. Move or cancel them first.");
            }

            bay.Deactivate();
        }
        else
        {
            bay.Activate();
        }

        await db.SaveChangesAsync(ct);
        return BayDto.From(bay);
    }

    // ---- working hours -----------------------------------------------------------------------

    public async Task<IReadOnlyList<WorkingDayDto>> GetWorkingWeekAsync(CancellationToken ct)
    {
        var days = await db.WorkingHours.AsNoTracking().ToListAsync(ct);
        return days.OrderBy(d => ((int)d.Day + 6) % 7) // Monday first
            .Select(d => new WorkingDayDto(d.Day, d.IsClosed, d.IsClosed ? null : d.Opens, d.IsClosed ? null : d.Closes))
            .ToList();
    }

    /// <summary>Replaces the whole week at once. Existing bookings are not touched.</summary>
    public async Task<IReadOnlyList<WorkingDayDto>> SetWorkingWeekAsync(IReadOnlyList<WorkingDayDto> week, CancellationToken ct)
    {
        await weekValidator.ValidateAndThrowAsync(week, ct);
        var replacement = week.Select(d => d.IsClosed
            ? WorkingHours.Closed(d.Day)
            : WorkingHours.Open(d.Day, d.Opens!.Value, d.Closes!.Value)).ToList();

        db.WorkingHours.RemoveRange(await db.WorkingHours.ToListAsync(ct));
        db.WorkingHours.AddRange(replacement);
        await db.SaveChangesAsync(ct);
        return await GetWorkingWeekAsync(ct);
    }

    private async Task<ServiceOffering> LoadServiceAsync(Guid id, CancellationToken ct) =>
        await db.Services.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Service", id);

    private async Task<Bay> LoadBayAsync(Guid id, CancellationToken ct) =>
        await db.Bays.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Bay", id);

    private async Task EnsureServiceNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Services.AnyAsync(s => s.Name == name && s.Id != exceptId, ct))
        {
            throw new ConflictException($"Service '{name}' already exists.");
        }
    }

    private async Task EnsureBayNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Bays.AnyAsync(b => b.Name == name && b.Id != exceptId, ct))
        {
            throw new ConflictException($"Bay '{name}' already exists.");
        }
    }
}
