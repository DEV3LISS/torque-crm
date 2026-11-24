using FluentValidation;
using Torque.Domain.Catalog;
using Torque.Domain.Scheduling;

namespace Torque.Application.Catalog;

public sealed record ServiceDto(Guid Id, string Name, string Category, int DurationMinutes, decimal Price, bool IsActive)
{
    public static ServiceDto From(ServiceOffering s) => new(s.Id, s.Name, s.Category, s.DurationMinutes, s.Price, s.IsActive);
}

public sealed record SaveServiceRequest(string Name, string Category, int DurationMinutes, decimal Price);

public sealed record BayDto(Guid Id, string Name, bool IsActive)
{
    public static BayDto From(Bay b) => new(b.Id, b.Name, b.IsActive);
}

public sealed record SaveBayRequest(string Name);

public sealed record WorkingDayDto(DayOfWeek Day, bool IsClosed, TimeOnly? Opens, TimeOnly? Closes);

public sealed class SaveServiceRequestValidator : AbstractValidator<SaveServiceRequest>
{
    public SaveServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ServiceOffering.MaxNameLength);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DurationMinutes).GreaterThan(0).LessThanOrEqualTo(ServiceOffering.MaxDurationMinutes)
            .Must(m => m % ServiceOffering.SlotMinutes == 0).WithMessage("Duration must be a multiple of 15 minutes.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, ignoreTrailingZeros: true);
    }
}

public sealed class SaveBayRequestValidator : AbstractValidator<SaveBayRequest>
{
    public SaveBayRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Bay.MaxNameLength);
    }
}

public sealed class WorkingWeekValidator : AbstractValidator<IReadOnlyList<WorkingDayDto>>
{
    public WorkingWeekValidator()
    {
        RuleFor(x => x).Must(w => w.Count == 7 && w.Select(d => d.Day).Distinct().Count() == 7)
            .WithMessage("Provide exactly one entry for each of the 7 days of the week.");
        RuleForEach(x => x).Must(d => d.IsClosed || (d.Opens is not null && d.Closes is not null))
            .WithMessage("Open days need both opening and closing time.");
    }
}
