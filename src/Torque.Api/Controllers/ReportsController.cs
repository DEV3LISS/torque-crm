using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Reports;

namespace Torque.Api.Controllers;

/// <summary>Owner's reports. Dates are local business dates, both inclusive (max one year).</summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    /// <summary>Net revenue (payments − refunds) by day, week or month, plus totals by payment method.</summary>
    [HttpGet("revenue")]
    public Task<RevenueReport> Revenue([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] RevenueGrouping groupBy = RevenueGrouping.Day, CancellationToken ct = default) =>
        reports.RevenueAsync(new ReportPeriod(from, to), groupBy, ct);

    /// <summary>Best-selling services by revenue (completed work orders, before order discounts).</summary>
    [HttpGet("top-services")]
    public Task<IReadOnlyList<TopServiceRow>> TopServices([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] int limit = 10, CancellationToken ct = default) =>
        reports.TopServicesAsync(new ReportPeriod(from, to), limit, ct);

    /// <summary>Booked time ÷ opening time per bay.</summary>
    [HttpGet("bay-utilization")]
    public Task<IReadOnlyList<BayUtilizationRow>> BayUtilization([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default) =>
        reports.BayUtilizationAsync(new ReportPeriod(from, to), ct);

    /// <summary>Completed work orders and revenue per mechanic.</summary>
    [HttpGet("mechanics")]
    public Task<IReadOnlyList<MechanicWorkloadRow>> Mechanics([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default) =>
        reports.MechanicWorkloadAsync(new ReportPeriod(from, to), ct);
}
