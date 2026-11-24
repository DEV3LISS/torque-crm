using Torque.Domain.Common;
using Torque.Domain.Users;

namespace Torque.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Create_normalizes_email_and_trims_name()
    {
        var user = User.Create("  Ivan.Petrov@Example.COM ", "  Ivan Petrov ", UserRole.Manager, "hash");

        user.Email.ShouldBe("ivan.petrov@example.com");
        user.FullName.ShouldBe("Ivan Petrov");
        user.IsActive.ShouldBeTrue();
        user.Id.Version.ShouldBe(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    public void Create_rejects_invalid_email(string email) =>
        Should.Throw<DomainException>(() => User.Create(email, "Name", UserRole.Admin, "hash"));

    [Fact]
    public void Create_rejects_empty_name() =>
        Should.Throw<DomainException>(() => User.Create("a@b.c", " ", UserRole.Admin, "hash"));

    [Fact]
    public void Deactivate_twice_is_an_invalid_state()
    {
        var user = User.Create("a@b.c", "Name", UserRole.Mechanic, "hash");
        user.Deactivate();

        user.IsActive.ShouldBeFalse();
        Should.Throw<InvalidStateException>(user.Deactivate);
    }
}
