using FluentValidation;
using Torque.Application.Common;
using Torque.Domain.Customers;

namespace Torque.Application.Customers;

public sealed record CustomerDto(Guid Id, string FullName, string Phone, string? Email, string? Notes, bool IsArchived, DateTimeOffset CreatedAt)
{
    public static CustomerDto From(Customer c) => new(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.IsArchived, c.CreatedAt);
}

public sealed record VehicleDto(Guid Id, Guid CustomerId, string Make, string Model, int? Year, string Plate, string? Vin)
{
    public static VehicleDto From(Vehicle v) => new(v.Id, v.CustomerId, v.Make, v.Model, v.Year, v.Plate, v.Vin);
}

public sealed record VisitDto(Guid AppointmentId, DateTimeOffset StartsAt, string Status, Guid VehicleId, string Plate);

/// <summary>Everything the front desk needs on one screen when a customer calls.</summary>
public sealed record CustomerCardDto(CustomerDto Customer, IReadOnlyList<VehicleDto> Vehicles, IReadOnlyList<VisitDto> RecentVisits, int TotalVisits);

public sealed record SaveCustomerRequest(string FullName, string Phone, string? Email, string? Notes);

public sealed record SaveVehicleRequest(string Make, string Model, int? Year, string Plate, string? Vin);

public sealed record CustomerSearchQuery(string? Search, PageRequest Paging, bool IncludeArchived = false);

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(Customer.MaxNameLength);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Notes).MaximumLength(Customer.MaxNotesLength);
    }
}

public sealed class SaveVehicleRequestValidator : AbstractValidator<SaveVehicleRequest>
{
    public SaveVehicleRequestValidator()
    {
        RuleFor(x => x.Make).NotEmpty().MaximumLength(Vehicle.MaxMakeModelLength);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(Vehicle.MaxMakeModelLength);
        RuleFor(x => x.Plate).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Vin).MaximumLength(17);
    }
}

public sealed class CustomerSearchQueryValidator : AbstractValidator<CustomerSearchQuery>
{
    public CustomerSearchQueryValidator()
    {
        RuleFor(x => x.Paging).ValidPage();
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
