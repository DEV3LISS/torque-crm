using Torque.Domain.Common;

namespace Torque.Domain.Auth;

/// <summary>
/// A long-lived token used to get a new short-lived access token without asking for the password again.
/// </summary>
/// <remarks>
/// Security design:
/// <list type="bullet">
/// <item>Only a SHA-256 hash is stored. A leaked database does not leak usable tokens.</item>
/// <item>Rotation: every refresh issues a new token and revokes the old one.</item>
/// <item>Family: all tokens that descend from one login share <see cref="FamilyId"/>. If an already
/// rotated token is presented again, somebody copied it — the whole family is revoked and the user
/// has to log in again.</item>
/// </list>
/// </remarks>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    /// <summary>Starts a new family — used at login.</summary>
    public static RefreshToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        Create(userId, tokenHash, Guid.CreateVersion7(), now, lifetime);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>True when the token was already exchanged for a newer one — a reuse means theft.</summary>
    public bool WasRotated => ReplacedById is not null;

    /// <summary>Exchanges this token for a new one in the same family.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (!IsActive(now))
        {
            throw new InvalidStateException("Refresh token is not active.");
        }

        var next = Create(UserId, newTokenHash, FamilyId, now, lifetime);
        RevokedAt = now;
        ReplacedById = next.Id;
        return next;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static RefreshToken Create(Guid userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("Token lifetime must be positive.");
        }

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }
}
