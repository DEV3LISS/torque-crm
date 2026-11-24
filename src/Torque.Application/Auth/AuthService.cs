using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Application.Users;
using Torque.Domain.Auth;
using Torque.Domain.Common;
using Torque.Domain.Users;

namespace Torque.Application.Auth;

/// <summary>
/// Login, token refresh and logout.
/// </summary>
public sealed partial class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokens,
    TimeProvider clock,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshRequest> refreshValidator,
    ILogger<AuthService> logger)
{
    // One message for every login failure: telling "no such user" apart from "wrong password"
    // would let an attacker find out which emails exist.
    private const string InvalidCredentials = "Invalid email or password.";
    private const string InvalidRefreshToken = "Refresh token is invalid or expired.";

    // A real hash of a random password. Used when the user does not exist, so that a login for an
    // unknown email takes as long as one for a known email (no timing side channel).
    private readonly Lazy<string> _dummyHash = new(() => passwordHasher.Hash(Guid.NewGuid().ToString()));

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);

        string email;
        try
        {
            email = User.NormalizeEmail(request.Email);
        }
        catch (DomainException)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            passwordHasher.Verify(_dummyHash.Value, request.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var check = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed || !user.IsActive)
        {
            LogLoginFailed(logger, user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(passwordHasher.Hash(request.Password));
        }

        var now = clock.GetUtcNow();
        var refreshToken = tokens.GenerateRefreshToken();
        var stored = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(refreshToken), now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(ct);

        LogLoggedIn(logger, user.Id);
        return BuildResponse(user, refreshToken, stored);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        await refreshValidator.ValidateAndThrowAsync(request, ct);

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException(InvalidRefreshToken);

        var now = clock.GetUtcNow();

        if (current.WasRotated)
        {
            // This token was already exchanged once. Presenting it again means two parties hold it —
            // treat as theft: kill every token of this login session.
            await RevokeFamilyAsync(current.FamilyId, now, ct);
            await db.SaveChangesAsync(ct);
            LogTokenReuse(logger, current.UserId, current.FamilyId);
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        if (!current.IsActive(now))
        {
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null || !user.IsActive)
        {
            current.Revoke(now);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var newRefreshToken = tokens.GenerateRefreshToken();
        var next = current.Rotate(tokens.HashRefreshToken(newRefreshToken), now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(next);

        // If two requests rotate the same token at the same moment, the optimistic concurrency token on
        // refresh_tokens makes the second SaveChanges fail instead of creating two valid children.
        await db.SaveChangesAsync(ct);

        return BuildResponse(user, newRefreshToken, next);
    }

    /// <summary>Idempotent: logging out with an unknown or already revoked token is not an error.</summary>
    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        await refreshValidator.ValidateAndThrowAsync(request, ct);

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
        {
            return;
        }

        token.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in family)
        {
            token.Revoke(now);
        }
    }

    private AuthResponse BuildResponse(User user, string refreshToken, RefreshToken stored)
    {
        var access = tokens.CreateAccessToken(user);
        return new AuthResponse(access.Token, access.ExpiresAt, refreshToken, stored.ExpiresAt, UserDto.From(user));
    }

    // Source-generated logging: no boxing/allocations, and the message template is checked at compile time.
    // Note: we log user ids, never emails or names (personal data stays out of logs).
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in")]
    private static partial void LogLoggedIn(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed login for user {UserId}")]
    private static partial void LogLoginFailed(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId}, family {FamilyId} revoked")]
    private static partial void LogTokenReuse(ILogger logger, Guid userId, Guid familyId);
}
