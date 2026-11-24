using System.Net;
using System.Net.Http.Json;
using Torque.Application.Auth;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Auth;

public class AuthTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_token_pair()
    {
        var auth = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.User.Email.ShouldBe(ApiFactory.AdminEmail);
        auth.AccessTokenExpiresAt.ShouldBeLessThan(auth.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Login_is_case_insensitive_for_email()
    {
        var auth = await Api.LoginAsync("  ADMIN@Test.Local ", ApiFactory.AdminPassword);

        auth.User.Email.ShouldBe(ApiFactory.AdminEmail);
    }

    [Theory]
    [InlineData(ApiFactory.AdminEmail, "wrong-password-123")]
    [InlineData("nobody@test.local", "whatever-password")]
    public async Task Wrong_credentials_give_the_same_401(string email, string password)
    {
        using var client = Api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        problem.GetProperty("detail").GetString().ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task Invalid_request_returns_400_with_field_errors()
    {
        using var client = Api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("not-an-email", ""));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("email", out _).ShouldBeTrue();
        errors.TryGetProperty("password", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        using var client = Api.CreateClient();

        var response = await client.GetAsync("/api/v1/users/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var first = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        using var client = Api.CreateClient();

        var second = await Refresh(client, first.RefreshToken);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = (await second.Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;
        rotated.RefreshToken.ShouldNotBe(first.RefreshToken);

        (await Refresh(client, first.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_session()
    {
        // Scenario: an attacker stole refresh token #1. The real user refreshes (gets #2).
        // The attacker then tries #1 → detected as reuse → #2 dies too, both must log in again.
        var first = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        using var client = Api.CreateClient();
        var rotated = (await (await Refresh(client, first.RefreshToken)).Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;

        (await Refresh(client, first.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await Refresh(client, rotated.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var auth = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        Api.Clock.Advance(TimeSpan.FromDays(15));
        using var client = Api.CreateClient();

        (await Refresh(client, auth.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parallel_refreshes_of_one_token_never_give_two_sessions()
    {
        var auth = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var client = Api.CreateClient();
            return await Refresh(client, auth.RefreshToken);
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(1);
        responses.ShouldAllBe(r =>
            r.StatusCode == HttpStatusCode.OK ||
            r.StatusCode == HttpStatusCode.Unauthorized ||
            r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        var auth = await Api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        using var client = Api.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(auth.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(auth.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Refresh(client, auth.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshToken));
}
