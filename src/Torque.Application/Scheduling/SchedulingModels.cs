using FluentValidation;
using Torque.Domain.Scheduling;

namespace Torque.Application.Scheduling;

public sealed record AvailabilityQuery(DateOnly Date, IReadOnlyList<Guid> ServiceIds, Guid? BayId = null);

public sealed record BaySlotsDto(Guid BayId, string BayName, IReadOnlyList<DateTimeOffset> Slots);

public sealed record AvailabilityDto(DateOnly Date, int DurationMinutes, IReadOnlyList<BaySlotsDto> Bays);

public sealed record BookAppointmentRequest(
    Guid CustomerId,
    Guid VehicleId,
    Guid BayId,
    DateTimeOffset StartsAt,
    IReadOnlyList<Guid> ServiceIds,
    string? Comment);

public sealed record RescheduleRequest(Guid BayId, DateTimeOffset StartsAt);

public sealed record CancelRequest(string? Reason);

public sealed record AppointmentListQuery(DateTimeOffset From, DateTimeOffset To, Guid? BayId = null, AppointmentStatus? Status = null);

public sealed record AppointmentDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid VehicleId,
    string VehiclePlate,
    string Vehicle,
    Guid BayId,
    string BayName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    IReadOnlyList<Guid> ServiceIds,
    string? Comment,
    string? CancellationReason);

public sealed class AvailabilityQueryValidator : AbstractValidator<AvailabilityQuery>
{
    public AvailabilityQueryValidator()
    {
        RuleFor(x => x.ServiceIds).NotEmpty().Must(ids => ids.Count <= 20);
    }
}

public sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.BayId).NotEmpty();
        RuleFor(x => x.StartsAt).NotEmpty();
        RuleFor(x => x.ServiceIds).NotEmpty().Must(ids => ids.Count <= 20);
        RuleFor(x => x.Comment).MaximumLength(Appointment.MaxCommentLength);
    }
}

public sealed class RescheduleRequestValidator : AbstractValidator<RescheduleRequest>
{
    public RescheduleRequestValidator()
    {
        RuleFor(x => x.BayId).NotEmpty();
        RuleFor(x => x.StartsAt).NotEmpty();
    }
}

public sealed class AppointmentListQueryValidator : AbstractValidator<AppointmentListQuery>
{
    public AppointmentListQueryValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From);
        RuleFor(x => x).Must(q => q.To - q.From <= TimeSpan.FromDays(62))
            .WithMessage("The period cannot be longer than 62 days.");
    }
}
