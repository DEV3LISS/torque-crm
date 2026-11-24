using Torque.Application.Abstractions;
using Torque.Infrastructure.Auth;

namespace Torque.UnitTests.Infrastructure;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void Correct_password_verifies_and_wrong_one_fails()
    {
        var hash = _sut.Hash("correct horse battery staple");

        hash.ShouldNotContain("correct horse");
        _sut.Verify(hash, "correct horse battery staple").ShouldBe(PasswordCheck.Success);
        _sut.Verify(hash, "Correct horse battery staple").ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public void Same_password_gives_different_hashes_because_of_salt() =>
        _sut.Hash("same-password-123").ShouldNotBe(_sut.Hash("same-password-123"));
}
