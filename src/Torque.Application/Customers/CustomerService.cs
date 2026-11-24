using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Customers;

namespace Torque.Application.Customers;

public sealed class CustomerService(
    IAppDbContext db,
    TimeProvider clock,
    IValidator<SaveCustomerRequest> customerValidator,
    IValidator<SaveVehicleRequest> vehicleValidator,
    IValidator<CustomerSearchQuery> searchValidator)
{
    private const int RecentVisitsShown = 10;

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct)
    {
        await customerValidator.ValidateAndThrowAsync(request, ct);
        var customer = Customer.Create(request.FullName, request.Phone, request.Email, request.Notes);
        await EnsurePhoneIsFreeAsync(customer.Phone, exceptId: null, ct);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return CustomerDto.From(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct)
    {
        await customerValidator.ValidateAndThrowAsync(request, ct);
        var customer = await LoadAsync(id, ct);
        customer.Update(request.FullName, request.Phone, request.Email, request.Notes);
        await EnsurePhoneIsFreeAsync(customer.Phone, exceptId: id, ct);

        await db.SaveChangesAsync(ct);
        return CustomerDto.From(customer);
    }

    public async Task<CustomerCardDto> GetCardAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer", id);

        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.CustomerId == id)
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .ToListAsync(ct);

        var visits = db.Appointments.AsNoTracking().Where(a => a.CustomerId == id);
        var totalVisits = await visits.CountAsync(ct);

        // One query with a join instead of N queries in a loop ("N+1 problem").
        var recent = await visits
            .OrderByDescending(a => a.StartsAt)
            .Take(RecentVisitsShown)
            .Join(db.Vehicles, a => a.VehicleId, v => v.Id,
                (a, v) => new { a.Id, a.StartsAt, a.Status, a.VehicleId, v.Plate })
            .ToListAsync(ct);

        return new CustomerCardDto(
            CustomerDto.From(customer),
            vehicles.Select(VehicleDto.From).ToList(),
            recent.Select(r => new VisitDto(r.Id, r.StartsAt, r.Status.ToString(), r.VehicleId, r.Plate)).ToList(),
            totalVisits);
    }

    /// <summary>
    /// One search box for the front desk: if the input looks like a phone number, match by digits
    /// (any format, partial: "3456789" finds "+79123456789"); otherwise match the name.
    /// </summary>
    public async Task<PagedResult<CustomerDto>> SearchAsync(CustomerSearchQuery query, CancellationToken ct)
    {
        await searchValidator.ValidateAndThrowAsync(query, ct);
        var customers = db.Customers.AsNoTracking();

        if (!query.IncludeArchived)
        {
            customers = customers.Where(c => !c.IsArchived);
        }

        var term = query.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var digits = PhoneNumber.DigitsOnly(term);
            if (digits.Length >= 4 && digits.Length * 2 >= term.Length)
            {
                // People type the domestic prefix: "8 912…" means "+7 912…" (stored form).
                // A leading 8 may also be a real digit in the middle of a number ("…3458…"), so match both.
                if (digits[0] == '8')
                {
                    var asInternational = "7" + digits[1..];
                    customers = customers.Where(c => c.Phone.Contains(digits) || c.Phone.StartsWith("+" + asInternational));
                }
                else
                {
                    customers = customers.Where(c => c.Phone.Contains(digits));
                }
            }
            else
            {
                // Provider-neutral LIKE on lower(); a trigram GIN index on lower(full_name)
                // (see the migration) makes "contains" searches fast even on 100k+ customers.
                var pattern = Search.ContainsPattern(term.ToLowerInvariant());
                // ToLower() here is translated to SQL lower() — it never runs in .NET, so culture rules don't apply.
#pragma warning disable CA1304, CA1311
                customers = customers.Where(c => EF.Functions.Like(c.FullName.ToLower(), pattern, @"\"));
#pragma warning restore CA1304, CA1311
            }
        }

        var total = await customers.CountAsync(ct);
        var items = await customers
            .OrderBy(c => c.FullName).ThenBy(c => c.Id)
            .Skip(query.Paging.Skip).Take(query.Paging.PageSize)
            .Select(c => new CustomerDto(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.IsArchived, c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }

    public async Task<CustomerDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var customer = await LoadAsync(id, ct);
        var now = clock.GetUtcNow();
        var hasFutureBookings = await db.Appointments.AnyAsync(
            a => a.CustomerId == id && a.StartsAt > now && Domain.Scheduling.Appointment.ActiveStatuses.Contains(a.Status), ct);
        if (hasFutureBookings)
        {
            throw new ConflictException("The customer has upcoming appointments. Cancel them first.");
        }

        customer.Archive();
        await db.SaveChangesAsync(ct);
        return CustomerDto.From(customer);
    }

    public async Task<CustomerDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var customer = await LoadAsync(id, ct);
        customer.Restore();
        await db.SaveChangesAsync(ct);
        return CustomerDto.From(customer);
    }

    public async Task<VehicleDto> AddVehicleAsync(Guid customerId, SaveVehicleRequest request, CancellationToken ct)
    {
        await vehicleValidator.ValidateAndThrowAsync(request, ct);
        var customer = await LoadAsync(customerId, ct);
        if (customer.IsArchived)
        {
            throw new ConflictException("Cannot add a vehicle to an archived customer.");
        }

        var vehicle = Vehicle.Create(customerId, request.Make, request.Model, request.Year, request.Plate, request.Vin, CurrentYear());
        await EnsurePlateIsFreeAsync(vehicle.Plate, exceptId: null, ct);

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return VehicleDto.From(vehicle);
    }

    public async Task<VehicleDto> UpdateVehicleAsync(Guid vehicleId, SaveVehicleRequest request, CancellationToken ct)
    {
        await vehicleValidator.ValidateAndThrowAsync(request, ct);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, ct)
            ?? throw new NotFoundException("Vehicle", vehicleId);

        vehicle.Update(request.Make, request.Model, request.Year, request.Plate, request.Vin, CurrentYear());
        await EnsurePlateIsFreeAsync(vehicle.Plate, exceptId: vehicleId, ct);

        await db.SaveChangesAsync(ct);
        return VehicleDto.From(vehicle);
    }

    /// <summary>"Whose car is this?" — lookup by plate in either keyboard layout.</summary>
    public async Task<VehicleDto> FindVehicleByPlateAsync(string plate, CancellationToken ct)
    {
        var normalized = LicensePlate.Normalize(plate);
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Plate == normalized, ct)
            ?? throw new NotFoundException("Vehicle", normalized);
        return VehicleDto.From(vehicle);
    }

    private int CurrentYear() => clock.GetUtcNow().Year;

    private async Task<Customer> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);

    // Friendly message for the common case; the unique index is what really guarantees it under races.
    private async Task EnsurePhoneIsFreeAsync(string phone, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.Phone == phone && c.Id != exceptId, ct))
        {
            throw new ConflictException($"A customer with phone {phone} already exists.");
        }
    }

    private async Task EnsurePlateIsFreeAsync(string plate, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Vehicles.AnyAsync(v => v.Plate == plate && v.Id != exceptId, ct))
        {
            throw new ConflictException($"A vehicle with plate {plate} is already registered.");
        }
    }
}
