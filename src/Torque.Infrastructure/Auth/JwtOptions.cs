using System.ComponentModel.DataAnnotations;

namespace Torque.Infrastructure.Auth;

/// <summary>
/// Bound from the "Jwt" configuration section and validated at startup: a missing or short signing
/// key stops the app immediately instead of failing on the first login.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    /// <summary>HMAC-SHA256 secret. At least 32 bytes (256 bits). Comes from an environment variable, never from git.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = null!;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
