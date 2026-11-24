using Torque.Domain.Auth;
using Torque.Domain.Common;

namespace Torque.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void Issued_token_is_active_until_it_expires()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", Now, Lifetime);

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now + Lifetime - TimeSpan.FromSeconds(1)).ShouldBeTrue();
        token.IsActive(Now + Lifetime).ShouldBeFalse();
    }

    [Fact]
    public void Rotate_revokes_old_token_and_keeps_the_family()
    {
        var old = RefreshToken.Issue(Guid.NewGuid(), "old", Now, Lifetime);
        var later = Now.AddHours(1);

        var next = old.Rotate("new", later, Lifetime);

        old.IsActive(later).ShouldBeFalse();
        old.WasRotated.ShouldBeTrue();
        old.ReplacedById.ShouldBe(next.Id);
        next.FamilyId.ShouldBe(old.FamilyId);
        next.UserId.ShouldBe(old.UserId);
        next.ExpiresAt.ShouldBe(later + Lifetime);
    }

    [Fact]
    public void Rotating_an_inactive_token_is_not_allowed()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", Now, Lifetime);
        token.Revoke(Now);

        Should.Throw<InvalidStateException>(() => token.Rotate("new", Now, Lifetime));
    }

    [Fact]
    public void Revoke_keeps_the_first_revocation_time()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", Now, Lifetime);

        token.Revoke(Now);
        token.Revoke(Now.AddDays(1));

        token.RevokedAt.ShouldBe(Now);
    }
}
