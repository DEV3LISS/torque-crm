using FluentValidation;
using Torque.Application.Abstractions;
using Torque.Application.Scheduling;

namespace Torque.Application.Reports;

public enum RevenueGrouping
{
    Day,
    Week,
    Month,
}

/// <summary>A reporting period in the business's local calendar, both dates inclusive.</summary>
public sealed record ReportPeriod(DateOnly From, DateOnly To);

public sealed record RevenueRow(DateOnly Period, decimal Payments, decimal Refunds, decimal Net, int PaymentCount);

public sealed record RevenueByMethodRow(string Method, decimal Net);

public sealed record RevenueReport(ReportPeriod Period, RevenueGrouping GroupBy, decimal Total, IReadOnlyList<RevenueRow> Rows,
    IReadOnlyList<RevenueByMethodRow> ByMethod);

public sealed record TopServiceRow(Guid? ServiceId, string Name, int TimesSold, decimal Revenue);

public sealed record BayUtilizationRow(Guid BayId, string BayName, int BookedMinutes, int OpenMinutes, decimal UtilizationPercent);

public sealed record BookedMinutesRow(Guid BayId, string BayName, int BookedMinutes);

public sealed record MechanicWorkloadRow(Guid MechanicId, string MechanicName, int CompletedOrders, decimal Revenue);

/// <summary>
/// Read model for reports. Implemented with hand-written SQL in Infrastructure: aggregations by local date,
/// time-zone conversion and range arithmetic are clearer (and faster) in SQL than in LINQ. See ADR-018.
/// All instants passed in are UTC.
/// </summary>
public interface IReportQueries
{
    Task<IReadOnlyList<RevenueRow>> RevenueAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, RevenueGrouping groupBy, string timeZone, CancellationToken ct);

    Task<IReadOnlyList<RevenueByMethodRow>> RevenueByMethodAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    Task<IReadOnlyList<TopServiceRow>> TopServicesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken ct);

    Task<IReadOnlyList<BookedMinutesRow>> BookedMinutesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    Task<IReadOnlyList<MechanicWorkloadRow>> MechanicWorkloadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}

public sealed class ReportPeriodValidator : AbstractValidator<ReportPeriod>
{
    public ReportPeriodValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x).Must(p => p.To.DayNumber - p.From.DayNumber <= 366).WithMessage("The period cannot exceed one year.");
    }
}

/// <summary>Owner's reports. Periods are local dates; conversion to UTC happens here, once.</summary>
public sealed class ReportService(
    IReportQueries queries,
    BusinessCalendarProvider calendars,
    IBusinessTimeZone timeZone,
    IValidator<ReportPeriod> validator)
{
    public async Task<RevenueReport> RevenueAsync(ReportPeriod period, RevenueGrouping groupBy, CancellationToken ct)
    {
        var (from, to) = await BoundsAsync(period, ct);
        var rows = await queries.RevenueAsync(from, to, groupBy, timeZone.TimeZone.Id, ct);
        var byMethod = await queries.RevenueByMethodAsync(from, to, ct);
        return new RevenueReport(period, groupBy, rows.Sum(r => r.Net), rows, byMethod);
    }

    public async Task<IReadOnlyList<TopServiceRow>> TopServicesAsync(ReportPeriod period, int limit, CancellationToken ct)
    {
        var (from, to) = await BoundsAsync(period, ct);
        return await queries.TopServicesAsync(from, to, Math.Clamp(limit, 1, 50), ct);
    }

    /// <summary>
    /// Booked time ÷ open time per bay. Open time comes from the opening hours (computed here, day by day,
    /// so closed days and different hours per weekday are handled); booked time from the database.
    /// </summary>
    public async Task<IReadOnlyList<BayUtilizationRow>> BayUtilizationAsync(ReportPeriod period, CancellationToken ct)
    {
        var (from, to) = await BoundsAsync(period, ct);
        var calendar = await calendars.GetAsync(ct);

        var openMinutes = 0;
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            if (calendar.OpeningWindow(day) is { } window)
            {
                openMinutes += (int)(window.Closes - window.Opens).TotalMinutes;
            }
        }

        var booked = await queries.BookedMinutesAsync(from, to, ct);
        return booked
            .Select(b => new BayUtilizationRow(b.BayId, b.BayName, b.BookedMinutes, openMinutes,
                openMinutes == 0 ? 0 : Math.Round(100m * b.BookedMinutes / openMinutes, 1)))
            .ToList();
    }

    public async Task<IReadOnlyList<MechanicWorkloadRow>> MechanicWorkloadAsync(ReportPeriod period, CancellationToken ct)
    {
        var (from, to) = await BoundsAsync(period, ct);
        return await queries.MechanicWorkloadAsync(from, to, ct);
    }

    /// <summary>[local From 00:00, local To+1 00:00) converted to UTC.</summary>
    private async Task<(DateTimeOffset From, DateTimeOffset To)> BoundsAsync(ReportPeriod period, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(period, ct);
        var zone = timeZone.TimeZone;
        return (StartOfLocalDay(period.From, zone), StartOfLocalDay(period.To.AddDays(1), zone));
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
}
