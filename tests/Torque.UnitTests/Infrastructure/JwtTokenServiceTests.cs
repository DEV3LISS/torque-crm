using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Torque.Domain.Users;
using Torque.Infrastructure.Auth;

namespace Torque.UnitTests.Infrastructure;

public class JwtTokenServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
    private readonly JwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 40),
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });
        _sut = new JwtTokenService(options, _clock);
    }

    [Fact]
    public void Access_token_contains_id_and_role_and_expires_in_15_minutes()
    {
        var user = User.Create("mech@torque.local", "Mechanic One", UserRole.Mechanic, "hash");

        var access = _sut.CreateAccessToken(user);
        var jwt = new JsonWebToken(access.Token);

        jwt.Subject.ShouldBe(user.Id.ToString());
        jwt.GetClaim("role").Value.ShouldBe("Mechanic");
        jwt.Issuer.ShouldBe("test-issuer");
        access.ExpiresAt.ShouldBe(_clock.GetUtcNow().AddMinutes(15));
        jwt.Claims.ShouldNotContain(c => c.Value == "mech@torque.local"); // no email in the token
    }

    [Fact]
    public void Refresh_tokens_are_random_and_hash_is_stable()
    {
        var a = _sut.GenerateRefreshToken();
        var b = _sut.GenerateRefreshToken();

        a.ShouldNotBe(b);
        a.Length.ShouldBeGreaterThanOrEqualTo(43); // 32 bytes in base64url

        var hash = _sut.HashRefreshToken(a);
        hash.Length.ShouldBe(64);
        _sut.HashRefreshToken(a).ShouldBe(hash);
        _sut.HashRefreshToken(b).ShouldNotBe(hash);
    }
}
