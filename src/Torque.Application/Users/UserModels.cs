using FluentValidation;
using Torque.Application.Common;
using Torque.Domain.Users;

namespace Torque.Application.Users;

public sealed record UserDto(Guid Id, string Email, string FullName, UserRole Role, bool IsActive, DateTimeOffset CreatedAt)
{
    public static UserDto From(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt);
}

public sealed record CreateUserRequest(string Email, string FullName, string Password, UserRole Role);

public sealed record ChangeRoleRequest(UserRole Role);

public sealed record UserListQuery(PageRequest Paging, bool IncludeInactive = false);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.MaxEmailLength);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.MaxNameLength);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class UserListQueryValidator : AbstractValidator<UserListQuery>
{
    public UserListQueryValidator()
    {
        RuleFor(x => x.Paging).ValidPage();
    }
}
