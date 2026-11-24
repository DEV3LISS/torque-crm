using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Torque.Api.Setup;
using Torque.Application.Auditing;
using Torque.Application.Common;

namespace Torque.Api.Controllers;

/// <summary>Who changed what and when. Admin only.</summary>
[ApiController]
[Route("api/v1/audit-logs")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AuditLogsController(AuditService audit) : ControllerBase
{
    /// <param name="entityType">E.g. WorkOrder, Customer, Payment.</param>
    /// <param name="entityId">Id of the entity.</param>
    /// <param name="userId">Who made the change.</param>
    /// <param name="from">From (inclusive).</param>
    /// <param name="to">To (exclusive).</param>
    /// <param name="page">Page number.</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    public Task<PagedResult<AuditLogDto>> List(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        audit.ListAsync(new AuditQuery(new PageRequest(page, pageSize), entityType, entityId, userId, from, to), ct);
}
