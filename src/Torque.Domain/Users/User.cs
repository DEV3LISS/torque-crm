using Torque.Domain.Common;

namespace Torque.Domain.Users;

/// <summary>
/// Fixed set of staff roles. Stored as text in the database (see ADR-004: no Roles table).
/// </summary>
public enum UserRole
{
    Admin,
    Manager,
    Mechanic,
}

/// <summary>
/// A staff member who can log in. Never hard-deleted: work orders, payments and the audit log reference users.
/// </summary>
public sealed class User : Entity, IHasTimestamps
{
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 200;

    // EF Core needs a parameterless constructor; private so nobody else can create an invalid user.
    private User() { }

    public string Email { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static User Create(string email, string fullName, UserRole role, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        return new User
        {
            Email = NormalizeEmail(email),
            FullName = fullName.Trim(),
            Role = role,
            PasswordHash = passwordHash,
            IsActive = true,
        };
    }

    /// <summary>
    /// Emails are compared case-insensitively. Normalising once on the way in means a plain
    /// unique index in the database is enough — no special collation needed.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException("A valid email is required.");
        }

        return email.Trim().ToLowerInvariant();
    }

    public void ChangeRole(UserRole role) => Role = role;

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        PasswordHash = passwordHash;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new InvalidStateException("User is already deactivated.");
        }

        IsActive = false;
    }

    public void Activate()
    {
        if (IsActive)
        {
            throw new InvalidStateException("User is already active.");
        }

        IsActive = true;
    }
}
