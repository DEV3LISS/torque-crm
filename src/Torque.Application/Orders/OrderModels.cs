using FluentValidation;
using Torque.Domain.Orders;

namespace Torque.Application.Orders;

public sealed record WorkOrderLineDto(Guid Id, LineKind Kind, Guid? ServiceId, string Name, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record PaymentDto(Guid Id, PaymentType Type, PaymentMethod Method, decimal Amount, Guid ReceivedById, DateTimeOffset CreatedAt)
{
    public static PaymentDto From(Payment p) => new(p.Id, p.Type, p.Method, p.Amount, p.ReceivedById, p.CreatedAt);
}

public sealed record WorkOrderDto(
    Guid Id,
    string Number,
    Guid? AppointmentId,
    Guid CustomerId,
    string CustomerName,
    Guid VehicleId,
    string VehiclePlate,
    Guid? MechanicId,
    WorkOrderStatus Status,
    DiscountType? DiscountType,
    decimal? DiscountValue,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    decimal PaidAmount,
    decimal AmountDue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<WorkOrderLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments);

public sealed record WorkOrderSummaryDto(
    Guid Id, string Number, string CustomerName, string VehiclePlate, WorkOrderStatus Status,
    decimal Total, decimal AmountDue, DateTimeOffset CreatedAt);

public sealed record OpenWorkOrderRequest(Guid? AppointmentId, Guid? CustomerId, Guid? VehicleId);

public sealed record AddServiceLineRequest(Guid ServiceId, decimal Quantity = 1);

public sealed record AddPartLineRequest(string Name, decimal Quantity, decimal UnitPrice);

public sealed record DiscountRequest(DiscountType Type, decimal Value);

public sealed record StartWorkRequest(Guid? MechanicId);

public sealed record PaymentRequest(decimal Amount, PaymentMethod Method);

public sealed record PaymentResult(PaymentDto Payment, WorkOrderDto WorkOrder, bool Replayed);

public sealed record WorkOrderListQuery(
    Common.PageRequest Paging, WorkOrderStatus? Status = null, Guid? CustomerId = null, Guid? MechanicId = null);

public static class WorkOrderNumber
{
    public static string Format(long number) => $"WO-{number}";
}

public sealed class OpenWorkOrderRequestValidator : AbstractValidator<OpenWorkOrderRequest>
{
    public OpenWorkOrderRequestValidator()
    {
        RuleFor(x => x).Must(r => r.AppointmentId is not null ^ (r.CustomerId is not null && r.VehicleId is not null))
            .WithMessage("Provide either appointmentId, or customerId and vehicleId for a walk-in.");
    }
}

public sealed class AddServiceLineRequestValidator : AbstractValidator<AddServiceLineRequest>
{
    public AddServiceLineRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1000).PrecisionScale(10, 2, true);
    }
}

public sealed class AddPartLineRequestValidator : AbstractValidator<AddPartLineRequest>
{
    public AddPartLineRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(WorkOrderLine.MaxNameLength);
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1000).PrecisionScale(10, 2, true);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true);
    }
}

public sealed class DiscountRequestValidator : AbstractValidator<DiscountRequest>
{
    public DiscountRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percent);
    }
}

public sealed class PaymentRequestValidator : AbstractValidator<PaymentRequest>
{
    public PaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(12, 2, true);
        RuleFor(x => x.Method).IsInEnum();
    }
}

public sealed class WorkOrderListQueryValidator : AbstractValidator<WorkOrderListQuery>
{
    public WorkOrderListQueryValidator()
    {
        RuleFor(x => x.Paging).Must(p => p.Page >= 1 && p.PageSize is >= 1 and <= Common.PageRequest.MaxPageSize);
    }
}
