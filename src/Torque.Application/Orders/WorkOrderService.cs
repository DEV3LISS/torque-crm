using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Common;
using Torque.Domain.Orders;
using Torque.Domain.Scheduling;
using Torque.Domain.Users;

namespace Torque.Application.Orders;

/// <summary>
/// Work orders: open (from an appointment or walk-in), add lines, discount, start, complete, cancel.
/// Money movements live in <see cref="PaymentService"/>.
/// </summary>
public sealed class WorkOrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<OpenWorkOrderRequest> openValidator,
    IValidator<AddServiceLineRequest> serviceLineValidator,
    IValidator<AddPartLineRequest> partLineValidator,
    IValidator<DiscountRequest> discountValidator,
    IValidator<WorkOrderListQuery> listValidator)
{
    public async Task<WorkOrderDto> OpenAsync(OpenWorkOrderRequest request, CancellationToken ct)
    {
        await openValidator.ValidateAndThrowAsync(request, ct);
        WorkOrder order;

        if (request.AppointmentId is { } appointmentId)
        {
            var appointment = await db.Appointments.AsNoTracking().Include(a => a.Services)
                .SingleOrDefaultAsync(a => a.Id == appointmentId, ct)
                ?? throw new NotFoundException("Appointment", appointmentId);

            if (appointment.Status != AppointmentStatus.CheckedIn)
            {
                throw new DomainException("A work order is opened when the car has arrived (appointment checked in).");
            }

            if (await db.WorkOrders.AnyAsync(w => w.AppointmentId == appointmentId, ct))
            {
                throw new ConflictException("A work order for this appointment already exists.");
            }

            order = WorkOrder.Open(appointment.CustomerId, appointment.VehicleId, appointmentId);

            // Pre-fill with the booked services at today's prices (snapshot).
            var serviceIds = appointment.Services.Select(s => s.ServiceId).ToList();
            var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToListAsync(ct);
            foreach (var service in services.OrderBy(s => s.Name))
            {
                order.AddService(service.Id, service.Name, service.Price, 1);
            }
        }
        else
        {
            var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == request.VehicleId, ct)
                ?? throw new NotFoundException("Vehicle", request.VehicleId!);
            if (vehicle.CustomerId != request.CustomerId)
            {
                throw new DomainException("The vehicle does not belong to this customer.");
            }

            order = WorkOrder.Open(vehicle.CustomerId, vehicle.Id, appointmentId: null);
        }

        db.WorkOrders.Add(order);
        await db.SaveChangesAsync(ct); // unique index on appointment_id is the real guard against duplicates
        return await GetAsync(order.Id, ct);
    }

    public async Task<WorkOrderDto> AddServiceAsync(Guid id, AddServiceLineRequest request, CancellationToken ct)
    {
        await serviceLineValidator.ValidateAndThrowAsync(request, ct);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId, ct)
            ?? throw new NotFoundException("Service", request.ServiceId);
        if (!service.IsActive)
        {
            throw new DomainException("This service is no longer offered.");
        }

        return await ChangeAsync(id, o => o.AddService(service.Id, service.Name, service.Price, request.Quantity), ct);
    }

    public async Task<WorkOrderDto> AddPartAsync(Guid id, AddPartLineRequest request, CancellationToken ct)
    {
        await partLineValidator.ValidateAndThrowAsync(request, ct);
        return await ChangeAsync(id, o => o.AddPart(request.Name, request.UnitPrice, request.Quantity), ct);
    }

    public Task<WorkOrderDto> RemoveLineAsync(Guid id, Guid lineId, CancellationToken ct) =>
        ChangeAsync(id, o => o.RemoveLine(lineId), ct);

    public async Task<WorkOrderDto> ApplyDiscountAsync(Guid id, DiscountRequest request, CancellationToken ct)
    {
        await discountValidator.ValidateAndThrowAsync(request, ct);
        var isAdmin = currentUser.Role == UserRole.Admin;
        return await ChangeAsync(id, o => o.ApplyDiscount(request.Type, request.Value, isAdmin), ct);
    }

    /// <summary>
    /// Start work. A mechanic always assigns themselves; front desk may assign any active mechanic.
    /// </summary>
    public async Task<WorkOrderDto> StartAsync(Guid id, StartWorkRequest request, CancellationToken ct)
    {
        var mechanicId = currentUser.Role == UserRole.Mechanic
            ? currentUser.Id!.Value
            : request.MechanicId ?? throw new DomainException("Choose the mechanic who will do the work.");

        var mechanicExists = await db.Users.AnyAsync(
            u => u.Id == mechanicId && u.IsActive && u.Role == UserRole.Mechanic, ct);
        if (!mechanicExists)
        {
            throw new DomainException("The assignee must be an active mechanic.");
        }

        return await ChangeAsync(id, o => o.Start(mechanicId), ct);
    }

    public Task<WorkOrderDto> CompleteAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, o => o.Complete(clock.GetUtcNow()), ct);

    public Task<WorkOrderDto> CancelAsync(Guid id, CancellationToken ct) => ChangeAsync(id, o => o.Cancel(), ct);

    public async Task<WorkOrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await db.WorkOrders.AsNoTracking().Include(w => w.Lines)
            .SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WorkOrder", id);

        var header = await db.Customers.AsNoTracking().Where(c => c.Id == order.CustomerId)
            .Join(db.Vehicles.Where(v => v.Id == order.VehicleId), c => c.Id, v => v.CustomerId,
                (c, v) => new { c.FullName, v.Plate })
            .SingleAsync(ct);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.WorkOrderId == id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

        return new WorkOrderDto(
            order.Id, WorkOrderNumber.Format(order.Number), order.AppointmentId, order.CustomerId, header.FullName,
            order.VehicleId, header.Plate, order.MechanicId, order.Status, order.DiscountType, order.DiscountValue,
            order.Subtotal, order.DiscountAmount, order.Total, order.PaidAmount, order.AmountDue,
            order.CreatedAt, order.CompletedAt, order.ClosedAt,
            order.Lines.OrderBy(l => l.Kind).ThenBy(l => l.Name)
                .Select(l => new WorkOrderLineDto(l.Id, l.Kind, l.ServiceId, l.Name, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList(),
            payments.Select(PaymentDto.From).ToList());
    }

    public async Task<PagedResult<WorkOrderSummaryDto>> ListAsync(WorkOrderListQuery query, CancellationToken ct)
    {
        await listValidator.ValidateAndThrowAsync(query, ct);
        var orders = db.WorkOrders.AsNoTracking();

        // A mechanic sees only their own work and the unassigned queue (least privilege).
        if (currentUser.Role == UserRole.Mechanic)
        {
            var me = currentUser.Id;
            orders = orders.Where(w => w.MechanicId == me || w.MechanicId == null);
        }

        if (query.Status is not null)
        {
            orders = orders.Where(w => w.Status == query.Status);
        }

        if (query.CustomerId is not null)
        {
            orders = orders.Where(w => w.CustomerId == query.CustomerId);
        }

        if (query.MechanicId is not null)
        {
            orders = orders.Where(w => w.MechanicId == query.MechanicId);
        }

        var total = await orders.CountAsync(ct);
        var page = await orders
            .OrderByDescending(w => w.Number)
            .Skip(query.Paging.Skip).Take(query.Paging.PageSize)
            .Join(db.Customers, w => w.CustomerId, c => c.Id, (w, c) => new { w, c.FullName })
            .Join(db.Vehicles, x => x.w.VehicleId, v => v.Id, (x, v) => new { x.w, x.FullName, v.Plate })
            .Select(x => new { x.w.Id, x.w.Number, x.FullName, x.Plate, x.w.Status, x.w.Total, x.w.PaidAmount, x.w.CreatedAt })
            .ToListAsync(ct);

        return new PagedResult<WorkOrderSummaryDto>(
            page.Select(x => new WorkOrderSummaryDto(x.Id, WorkOrderNumber.Format(x.Number), x.FullName, x.Plate, x.Status,
                x.Total, x.Total - x.PaidAmount, x.CreatedAt)).ToList(),
            query.Paging.Page, query.Paging.PageSize, total);
    }

    private async Task<WorkOrderDto> ChangeAsync(Guid id, Action<WorkOrder> change, CancellationToken ct)
    {
        var order = await db.WorkOrders.Include(w => w.Lines).SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WorkOrder", id);

        EnsureMechanicMayTouch(order);
        change(order);

        // xmin concurrency token: if someone else changed this order since we loaded it, SaveChanges
        // fails with 409 instead of silently overwriting their lines/totals (lost update).
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// IDOR protection: knowing an order id is not enough. A mechanic may change only an order assigned
    /// to them (or an unassigned one, which they take by starting it).
    /// </summary>
    private void EnsureMechanicMayTouch(WorkOrder order)
    {
        if (currentUser.Role == UserRole.Mechanic && order.MechanicId is not null && order.MechanicId != currentUser.Id)
        {
            throw new ForbiddenException("This work order is assigned to another mechanic.");
        }
    }
}
