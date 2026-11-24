using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Scheduling;
using Torque.Domain.Scheduling;

namespace Torque.Api.Controllers;

/// <summary>Free slots for booking.</summary>
[ApiController]
[Route("api/v1/availability")]
[Produces("application/json")]
public sealed class AvailabilityController(AppointmentService appointments) : ControllerBase
{
    /// <summary>Free start times per bay for a local date and a set of services (15-minute grid).</summary>
    /// <param name="date">Local business date, e.g. 2026-12-01.</param>
    /// <param name="serviceIds">Repeat the parameter for several services.</param>
    /// <param name="bayId">Optional: only this bay.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    public Task<AvailabilityDto> Get(
        [FromQuery] DateOnly date,
        [FromQuery] Guid[] serviceIds,
        [FromQuery] Guid? bayId,
        CancellationToken ct) =>
        appointments.GetAvailabilityAsync(new AvailabilityQuery(date, serviceIds, bayId), ct);
}

/// <summary>
/// Appointments. Front desk books, moves and cancels; mechanics see the board and move cars through the bay.
/// </summary>
[ApiController]
[Route("api/v1/appointments")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class AppointmentsController(AppointmentService appointments) : ControllerBase
{
    /// <summary>Schedule board for a period (max 62 days).</summary>
    [HttpGet]
    public Task<IReadOnlyList<AppointmentDto>> List(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] Guid? bayId,
        [FromQuery] AppointmentStatus? status,
        CancellationToken ct) =>
        appointments.ListAsync(new AppointmentListQuery(from, to, bayId, status), ct);

    [HttpGet("{id:guid}")]
    public Task<AppointmentDto> Get(Guid id, CancellationToken ct) => appointments.GetAsync(id, ct);

    /// <summary>Book a slot. 409 if the bay is already taken at that time (guaranteed by the database).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.FrontDesk)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AppointmentDto>> Book(BookAppointmentRequest request, CancellationToken ct)
    {
        var appointment = await appointments.BookAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = appointment.Id }, appointment);
    }

    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Policy = Policies.FrontDesk)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<AppointmentDto> Reschedule(Guid id, RescheduleRequest request, CancellationToken ct) =>
        appointments.RescheduleAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.FrontDesk)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AppointmentDto> Cancel(Guid id, CancelRequest request, CancellationToken ct) =>
        appointments.CancelAsync(id, request, ct);

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = Policies.FrontDesk)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AppointmentDto> NoShow(Guid id, CancellationToken ct) => appointments.MarkNoShowAsync(id, ct);

    /// <summary>The car has arrived (any staff).</summary>
    [HttpPost("{id:guid}/check-in")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AppointmentDto> CheckIn(Guid id, CancellationToken ct) => appointments.CheckInAsync(id, ct);

    /// <summary>Work is done (any staff).</summary>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AppointmentDto> Complete(Guid id, CancellationToken ct) => appointments.CompleteAsync(id, ct);
}
