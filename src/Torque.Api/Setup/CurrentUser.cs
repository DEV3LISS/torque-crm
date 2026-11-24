using Microsoft.IdentityModel.JsonWebTokens;
using Torque.Application.Abstractions;
using Torque.Domain.Users;
using Torque.Infrastructure.Auth;

namespace Torque.Api.Setup;

/// <summary>Who is calling, taken from the validated JWT. Null outside an HTTP request (seeding, CLI).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(accessor.HttpContext?.User.FindFirst(ClaimNames.Role)?.Value, out var role) ? role : null;

    public string? CorrelationId => accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
}
