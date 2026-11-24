using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Users;

namespace Torque.Application.Users;

/// <summary>
/// Staff account management. Authorization (who may call what) is enforced by the API policies;
/// this class enforces the business rules that do not depend on HTTP.
/// </summary>
public sealed class UserService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<CreateUserRequest> createValidator,
    IValidator<ChangeRoleRequest> changeRoleValidator,
    IValidator<UserListQuery> listValidator)
{
    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException($"A user with email '{email}' already exists.");
        }

        var user = User.Create(email, request.FullName, request.Role, passwordHasher.Hash(request.Password));
        db.Users.Add(user);

        // The unique index on email is the real guard (two admins creating the same email at once);
        // the AnyAsync check above just gives a friendlier message in the common case.
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    /// <summary>
    /// Creates the very first Admin. Used by the "create-admin" CLI command and the dev seed —
    /// there is deliberately no public HTTP endpoint that can create an admin without being an admin.
    /// </summary>
    /// <returns>false if any user already exists (nothing is changed).</returns>
    public async Task<bool> CreateInitialAdminAsync(string email, string fullName, string password, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return false;
        }

        await CreateAsync(new CreateUserRequest(email, fullName, password, UserRole.Admin), ct);
        return true;
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User", id);
        return UserDto.From(user);
    }

    public async Task<UserDto> GetCurrentAsync(CancellationToken ct)
    {
        var id = currentUser.Id ?? throw new UnauthorizedException("Not authenticated.");
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        await listValidator.ValidateAndThrowAsync(query, ct);

        var users = db.Users.AsNoTracking();
        if (!query.IncludeInactive)
        {
            users = users.Where(u => u.IsActive);
        }

        var total = await users.CountAsync(ct);
        var page = await users
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id) // stable order: equal names would otherwise jump between pages
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<UserDto>(page, query.Paging.Page, query.Paging.PageSize, total);
    }

    public async Task<UserDto> ChangeRoleAsync(Guid id, ChangeRoleRequest request, CancellationToken ct)
    {
        await changeRoleValidator.ValidateAndThrowAsync(request, ct);
        var user = await LoadAsync(id, ct);

        if (user.Role == UserRole.Admin && request.Role != UserRole.Admin)
        {
            await EnsureNotLastActiveAdminAsync(user, ct);
        }

        user.ChangeRole(request.Role);
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    public async Task<UserDto> DeactivateAsync(Guid id, CancellationToken ct)
    {
        if (currentUser.Id == id)
        {
            throw new ConflictException("You cannot deactivate your own account.");
        }

        var user = await LoadAsync(id, ct);
        if (user.Role == UserRole.Admin)
        {
            await EnsureNotLastActiveAdminAsync(user, ct);
        }

        user.Deactivate();

        // Log the user out everywhere. Their current access token still works until it expires
        // (max 15 minutes) — the accepted trade-off of stateless JWTs.
        var now = clock.GetUtcNow();
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in activeTokens)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    public async Task<UserDto> ActivateAsync(Guid id, CancellationToken ct)
    {
        var user = await LoadAsync(id, ct);
        user.Activate();
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    private async Task<User> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("User", id);

    /// <summary>The business must never lock itself out: at least one active Admin must remain.</summary>
    private async Task EnsureNotLastActiveAdminAsync(User user, CancellationToken ct)
    {
        var otherAdmins = await db.Users.CountAsync(
            u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct);

        if (otherAdmins == 0)
        {
            throw new ConflictException("At least one active Admin must remain.");
        }
    }
}
