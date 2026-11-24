using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Common;
using Torque.Application.Customers;

namespace Torque.Api.Controllers;

/// <summary>Customers and their vehicles. Front desk (Admin, Manager).</summary>
[ApiController]
[Route("api/v1/customers")]
[Authorize(Policy = Policies.FrontDesk)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    /// <summary>Search by phone (any format, partial) or by part of the name.</summary>
    [HttpGet]
    public Task<PagedResult<CustomerDto>> Search(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default) =>
        customers.SearchAsync(new CustomerSearchQuery(search, new PageRequest(page, pageSize), includeArchived), ct);

    /// <summary>Customer card: details, vehicles, recent visits.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CustomerCardDto> Get(Guid id, CancellationToken ct) => customers.GetCardAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CustomerDto> Update(Guid id, SaveCustomerRequest request, CancellationToken ct) =>
        customers.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CustomerDto> Archive(Guid id, CancellationToken ct) => customers.ArchiveAsync(id, ct);

    [HttpPost("{id:guid}/restore")]
    public Task<CustomerDto> Restore(Guid id, CancellationToken ct) => customers.RestoreAsync(id, ct);

    [HttpPost("{id:guid}/vehicles")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehicleDto>> AddVehicle(Guid id, SaveVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await customers.AddVehicleAsync(id, request, ct);
        return CreatedAtAction(nameof(Get), new { id }, vehicle);
    }
}

/// <summary>Vehicles: edit and look up by plate ("whose car is this?").</summary>
[ApiController]
[Route("api/v1/vehicles")]
[Authorize(Policy = Policies.FrontDesk)]
[Produces("application/json")]
public sealed class VehiclesController(CustomerService customers) : ControllerBase
{
    /// <summary>Find a vehicle by plate, typed in Cyrillic or Latin, with or without spaces.</summary>
    [HttpGet]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<VehicleDto> FindByPlate([FromQuery] string plate, CancellationToken ct) =>
        customers.FindVehicleByPlateAsync(plate, ct);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VehicleDto> Update(Guid id, SaveVehicleRequest request, CancellationToken ct) =>
        customers.UpdateVehicleAsync(id, request, ct);
}
