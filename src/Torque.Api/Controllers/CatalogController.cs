using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Catalog;

namespace Torque.Api.Controllers;

/// <summary>Service catalog. Everyone can read; only Admin changes prices.</summary>
[ApiController]
[Route("api/v1/services")]
[Produces("application/json")]
public sealed class ServicesController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ServiceDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        catalog.ListServicesAsync(includeInactive, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceDto>> Create(SaveServiceRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateServiceAsync(request, ct));

    /// <summary>Changing a price does not change existing orders (they keep a price snapshot).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<ServiceDto> Update(Guid id, SaveServiceRequest request, CancellationToken ct) =>
        catalog.UpdateServiceAsync(id, request, ct);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<ServiceDto> Deactivate(Guid id, CancellationToken ct) => catalog.SetServiceActiveAsync(id, false, ct);

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<ServiceDto> Activate(Guid id, CancellationToken ct) => catalog.SetServiceActiveAsync(id, true, ct);
}

/// <summary>Service bays.</summary>
[ApiController]
[Route("api/v1/bays")]
[Produces("application/json")]
public sealed class BaysController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BayDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        catalog.ListBaysAsync(includeInactive, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<BayDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BayDto>> Create(SaveBayRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateBayAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<BayDto> Rename(Guid id, SaveBayRequest request, CancellationToken ct) => catalog.RenameBayAsync(id, request, ct);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BayDto> Deactivate(Guid id, CancellationToken ct) => catalog.SetBayActiveAsync(id, false, ct);

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<BayDto> Activate(Guid id, CancellationToken ct) => catalog.SetBayActiveAsync(id, true, ct);
}

/// <summary>Opening hours (business local time).</summary>
[ApiController]
[Route("api/v1/working-hours")]
[Produces("application/json")]
public sealed class WorkingHoursController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WorkingDayDto>> Get(CancellationToken ct) => catalog.GetWorkingWeekAsync(ct);

    /// <summary>Replace the whole week (7 entries).</summary>
    [HttpPut]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IReadOnlyList<WorkingDayDto>> Set(IReadOnlyList<WorkingDayDto> week, CancellationToken ct) =>
        catalog.SetWorkingWeekAsync(week, ct);
}
