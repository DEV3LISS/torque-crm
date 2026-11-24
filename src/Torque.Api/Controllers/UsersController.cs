using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Common;
using Torque.Application.Users;

namespace Torque.Api.Controllers;

/// <summary>Staff account management — Admin only.</summary>
/// <remarks>
/// "GET /users/me" lives in <see cref="MeController"/> on purpose: authorization attributes on a
/// controller and on an action are combined (ALL must pass), so a plain [Authorize] on an action here
/// would NOT relax the controller's AdminOnly policy.
/// </remarks>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default) =>
        users.ListAsync(new UserListQuery(new PageRequest(page, pageSize), includeInactive), ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<UserDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserDto> ChangeRole(Guid id, ChangeRoleRequest request, CancellationToken ct) =>
        users.ChangeRoleAsync(id, request, ct);

    /// <summary>Deactivate and log out everywhere. Users are never deleted (history references them).</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserDto> Deactivate(Guid id, CancellationToken ct) => users.DeactivateAsync(id, ct);

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserDto> Activate(Guid id, CancellationToken ct) => users.ActivateAsync(id, ct);
}
