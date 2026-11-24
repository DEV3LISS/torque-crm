using Microsoft.AspNetCore.Identity;
using Torque.Application.Abstractions;
using Torque.Domain.Users;

namespace Torque.Infrastructure.Auth;

/// <summary>
/// Thin adapter over ASP.NET Core Identity's PasswordHasher: PBKDF2 with HMAC-SHA512,
/// 100 000 iterations, random salt per password, versioned format (so iterations can be raised later
/// and old hashes upgraded transparently on next login — see PasswordCheck.SuccessRehashNeeded).
/// We do not write our own crypto.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    // The User argument is unused by the default implementation, so null is safe here.
    public string Hash(string password) => _inner.HashPassword(null!, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
}
